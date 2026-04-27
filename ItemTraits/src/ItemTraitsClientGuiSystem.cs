using System;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace ItemTraits;

public sealed class ItemTraitsClientGuiSystem : ModSystem
{
    private const string ToggleBeltHotkeyCode = "itemtraits-toggle-belt-panel";
    private const string DebugPrefix = "[DragoonDebug]";

    /// <summary>Flip to <c>true</c> in a debugger or temporarily in code to log belt UI steps via <see cref="DebugChat"/>.</summary>
    private static bool BeltGuiDebug = false;

    private ICoreClientAPI? capi;
    private GuiDialogDragoonBelt? beltDialog;
    private long tick;
    private bool beltInvOpened;
    private bool beltPanelRequested;
    /// <summary>When the player closes the belt panel while the character/inventory (E) UI is still up, we keep the panel from auto-reopening until E is closed, or the belt hotkey explicitly re-opens it.</summary>
    private bool beltPanelDismissedWhileMainInvOpen;
    private long nextDebugMessageMs;
    private long nextBeltAttrSyncMs;

    /// <summary>Client IPlayerInventoryManager may not return custom inventories from GetOwnInventory/GetInventory even after we assign <see cref="IPlayerInventoryManager.Inventories"/>; keep the instance we create.</summary>
    private string? clientBeltCachePlayerUid;
    private DragoonBeltInventory? clientBeltCache;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        capi = api;
        // IInputAPI.RegisterHotKey: parameters are (alt, ctrl, shift) — not (ctrl, shift, alt). Wrong order previously registered Alt+Ctrl+B.
        api.Input.RegisterHotKey(
            ToggleBeltHotkeyCode,
            "ItemTraits: Toggle Dragoon Belt Panel (Ctrl+Shift+B)",
            GlKeys.B,
            HotkeyType.GUIOrOtherControls,
            false,
            true,
            true
        );
        api.Input.SetHotKeyHandler(ToggleBeltHotkeyCode, OnToggleBeltPanelHotkey);
        tick = api.Event.RegisterGameTickListener(OnClientTick, 200, 0);
    }

    public override void Dispose()
    {
        if (capi is not null)
        {
            capi.Event.UnregisterGameTickListener(tick);
        }

        if (beltDialog is not null)
        {
            beltDialog.OnClosed -= OnBeltDialogClosedByUser;
            beltDialog.TryClose();
            beltDialog.Dispose();
            beltDialog = null;
        }

        IPlayer? p = capi?.World?.Player;
        if (p is not null && beltInvOpened)
        {
            IInventory? inv = p.InventoryManager.GetOwnInventory(ItemTraitsConstants.BeltInventoryClassName);
            if (inv is null && clientBeltCache is not null && p.PlayerUID == clientBeltCachePlayerUid)
            {
                inv = clientBeltCache;
            }

            if (inv is not null)
            {
                p.InventoryManager.CloseInventoryAndSync(inv);
            }
        }

        beltInvOpened = false;
    }

    private void OnClientTick(float _dt)
    {
        if (capi?.World?.Player is null)
        {
            return;
        }

        IPlayer pl = capi.World.Player;
        EnsureBeltInventory(pl);

        bool invOpen = IsInventoryUiOpen();
        if (!invOpen)
        {
            beltPanelDismissedWhileMainInvOpen = false;
        }

        bool beltActive = IsBeltActive(pl.Entity);
        if (!beltActive)
        {
            beltPanelRequested = false;
            beltPanelDismissedWhileMainInvOpen = false;
            clientBeltCache = null;
            clientBeltCachePlayerUid = null;
            CloseBeltDialog();
            return;
        }

        bool wantBeltPanel = beltPanelRequested
            || (invOpen && !beltPanelDismissedWhileMainInvOpen);
        if (wantBeltPanel)
        {
            SyncBeltFromEntityThrottled(pl, force: false);
            EnsureBeltDialog();
            beltDialog?.Refresh(BuildPanelString());
            return;
        }

        CloseBeltDialog();
    }

    private void EnsureBeltInventory(IPlayer player)
    {
        if (capi is null)
        {
            return;
        }

        IPlayerInventoryManager invMan = player.InventoryManager;
        if (clientBeltCachePlayerUid != player.PlayerUID)
        {
            clientBeltCache = null;
            clientBeltCachePlayerUid = player.PlayerUID;
        }

        if (invMan.GetOwnInventory(ItemTraitsConstants.BeltInventoryClassName) is DragoonBeltInventory dFromManager)
        {
            clientBeltCache = dFromManager;
            return;
        }

        foreach (InventoryBase ib in invMan.InventoriesOrdered)
        {
            if (ib is DragoonBeltInventory dOrdered)
            {
                clientBeltCache = dOrdered;
                return;
            }
        }

        if (clientBeltCache is not null)
        {
            return;
        }

        string? expected = null;
        try
        {
            expected = invMan.GetInventoryName(ItemTraitsConstants.BeltInventoryClassName);
        }
        catch
        {
        }

        if (string.IsNullOrEmpty(expected))
        {
            expected = $"{ItemTraitsConstants.BeltInventoryClassName}-{player.PlayerUID}";
        }

        IInventory? existing = invMan.GetInventory(expected);
        if (existing is DragoonBeltInventory existingDb)
        {
            clientBeltCache = existingDb;
            return;
        }

        if (existing is not null)
        {
            invMan.Inventories.Remove(expected);
        }

        var created = new DragoonBeltInventory(player.PlayerUID, capi);
        try
        {
            invMan.Inventories[created.InventoryID] = created;
            if (!string.Equals(expected, created.InventoryID, StringComparison.Ordinal))
            {
                invMan.Inventories[expected] = created;
            }
        }
        catch
        {
            // Some client builds use a read-only or differently keyed map; the dialog only needs a stable instance + sync.
        }

        clientBeltCache = created;
        SyncBeltFromEntity(created, player);
    }

    private void SyncBeltFromEntityThrottled(IPlayer player, bool force)
    {
        if (clientBeltCache is null || capi is null)
        {
            return;
        }

        long now = capi.World.ElapsedMilliseconds;
        if (!force && now < nextBeltAttrSyncMs)
        {
            return;
        }

        nextBeltAttrSyncMs = now + 750;
        SyncBeltFromEntity(clientBeltCache, player);
    }

    private static void SyncBeltFromEntity(DragoonBeltInventory inv, IPlayer? player)
    {
        if (player?.Entity is not EntityPlayer e)
        {
            return;
        }

        ITreeAttribute? tree = e.WatchedAttributes.GetTreeAttribute(ItemTraitsConstants.BeltInventoryTreeKey);
        if (tree is null)
        {
            return;
        }

        inv.FromTreeAttributes(tree);
    }

    private void EnsureBeltDialog()
    {
        if (capi?.World?.Player is null)
        {
            return;
        }

        IPlayer pl = capi.World.Player;
        IInventory? beltInv = GetBeltInventory(pl);
        if (beltInv is null)
        {
            DebugChat("belt inventory not found on client inventory manager.");
            return;
        }

        SyncBeltFromEntityThrottled(pl, force: !beltInvOpened);

        object? openPacket = capi.World.Player.InventoryManager.OpenInventory(beltInv);
        if (openPacket is not null)
        {
            capi.Network.SendPacketClient(openPacket);
            beltInvOpened = true;
            DebugChat($"opened inventory '{beltInv.InventoryID}' (slots: {beltInv.Count}).");
        }

        if (beltDialog is null)
        {
            beltDialog = new GuiDialogDragoonBelt(capi, beltInv, BuildPanelString());
            beltDialog.OnClosed += OnBeltDialogClosedByUser;
            DebugChat("created GuiDialogDragoonBelt instance.");
        }

        if (!beltDialog.IsOpened())
        {
            beltDialog.TryOpen();
            DebugChat($"TryOpen called. IsOpened={beltDialog.IsOpened()}");
        }
    }

    private void OnBeltDialogClosedByUser()
    {
        beltPanelRequested = false;
        if (IsInventoryUiOpen())
        {
            beltPanelDismissedWhileMainInvOpen = true;
        }
    }

    private IInventory? GetBeltInventory(IPlayer player)
    {
        if (clientBeltCache is not null && clientBeltCachePlayerUid == player.PlayerUID)
        {
            return clientBeltCache;
        }

        IPlayerInventoryManager im = player.InventoryManager;
        IInventory? own = im.GetOwnInventory(ItemTraitsConstants.BeltInventoryClassName);
        if (own is not null)
        {
            return own;
        }

        string? expected;
        try
        {
            expected = im.GetInventoryName(ItemTraitsConstants.BeltInventoryClassName);
        }
        catch
        {
            expected = null;
        }

        if (string.IsNullOrEmpty(expected))
        {
            expected = $"{ItemTraitsConstants.BeltInventoryClassName}-{player.PlayerUID}";
        }

        IInventory? byName = im.GetInventory(expected);
        if (byName is not null)
        {
            return byName;
        }

        IInventory? bySuffix = im.GetInventory($"{ItemTraitsConstants.BeltInventoryClassName}-{player.PlayerUID}");
        if (bySuffix is not null)
        {
            return bySuffix;
        }

        foreach (InventoryBase ib in im.InventoriesOrdered)
        {
            if (ib is DragoonBeltInventory d)
            {
                return d;
            }
        }

        return null;
    }

    private bool IsInventoryUiOpen()
    {
        if (capi is null)
        {
            return false;
        }

        for (int i = 0; i < capi.Gui.LoadedGuis.Count; i++)
        {
            string name = capi.Gui.LoadedGuis[i].GetType().Name;
            if (name.Contains("GuiDialogCharacter", StringComparison.Ordinal)
                || name.Contains("GuiDialogInventory", StringComparison.Ordinal)
                || (name.Contains("Backpack", StringComparison.Ordinal) && name.Contains("Gui", StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }

    private bool OnToggleBeltPanelHotkey(KeyCombination _comb)
    {
        if (capi?.World?.Player?.Entity is not EntityPlayer e)
        {
            return false;
        }

        if (!IsBeltActive(e))
        {
            capi.ShowChatMessage("Dragoon Belt panel requires the Dragoon Belt to be equipped.");
            beltPanelRequested = false;
            CloseBeltDialog();
            return false;
        }

        beltPanelRequested = !beltPanelRequested;
        if (beltPanelRequested)
        {
            beltPanelDismissedWhileMainInvOpen = false;
        }

        capi.ShowChatMessage($"Dragoon Belt panel toggle: {(beltPanelRequested ? "ON" : "OFF")}");
        if (!beltPanelRequested)
        {
            CloseBeltDialog();
        }

        return true;
    }

    private void CloseBeltDialog()
    {
        if (beltDialog is not null)
        {
            beltDialog.OnClosed -= OnBeltDialogClosedByUser;
            GuiDialogDragoonBelt closed = beltDialog;
            beltDialog = null;
            closed.TryClose();
            if (capi is not null)
            {
                capi.Event.RegisterCallback(
                    _ =>
                    {
                        try
                        {
                            closed.Dispose();
                        }
                        catch
                        {
                            // Ignore: dialog may already be torn down by the GUI manager.
                        }
                    },
                    50
                );
            }
            else
            {
                try
                {
                    closed.Dispose();
                }
                catch
                {
                }
            }

            DebugChat("belt dialog close requested.");
        }

        if (capi?.World?.Player is null || !beltInvOpened)
        {
            beltInvOpened = false;
            return;
        }

        IInventory? inv = capi.World.Player.InventoryManager.GetOwnInventory(ItemTraitsConstants.BeltInventoryClassName);
        if (inv is null && clientBeltCache is not null && capi.World.Player.PlayerUID == clientBeltCachePlayerUid)
        {
            inv = clientBeltCache;
        }

        if (inv is null)
        {
            beltInvOpened = false;
            return;
        }

        capi.World.Player.InventoryManager.CloseInventoryAndSync(inv);
        beltInvOpened = false;
    }

    private void DebugChat(string message)
    {
        if (!BeltGuiDebug)
        {
            return;
        }

        if (capi?.World is null)
        {
            return;
        }

        long now = capi.World.ElapsedMilliseconds;
        if (now < nextDebugMessageMs)
        {
            return;
        }

        nextDebugMessageMs = now + 1200;
        capi.ShowChatMessage($"{DebugPrefix} {message}");
    }

    private string BuildPanelString()
    {
        if (capi?.World?.Player?.Entity is not EntityPlayer e)
        {
            return Lang.Get("itemtraits:gui-no-player");
        }

        ITreeAttribute? t = e.WatchedAttributes.GetTreeAttribute(ItemTraitsConstants.TreeKey);
        bool shoes = t?.GetBool("dragoonShoes", false) ?? false;
        bool belt = t?.GetBool("dragoonBelt", false) ?? false;
        bool helm = t?.GetBool("dragoonHelm", false) ?? false;
        bool shirt = t?.GetBool("dragoonShirt", false) ?? false;
        bool full = t?.GetBool("fullSet", false) ?? false;

        var sb = new StringBuilder(260);
        sb.AppendLine(Lang.Get("itemtraits:gui-line-shoes", Status(shoes)));
        sb.AppendLine(Lang.Get("itemtraits:gui-line-belt", Status(belt)));
        sb.AppendLine(Lang.Get("itemtraits:gui-line-helm", Status(helm)));
        sb.AppendLine(Lang.Get("itemtraits:gui-line-shirt", Status(shirt)));
        sb.AppendLine();
        sb.AppendLine(Lang.Get("itemtraits:gui-line-flight", Status(full)));
        sb.AppendLine(Lang.Get("itemtraits:gui-line-beltinv"));
        return sb.ToString();
    }

    private static string Status(bool on) => on ? Lang.Get("itemtraits:status-active") : Lang.Get("itemtraits:status-inactive");

    private static bool IsBeltActive(EntityPlayer? e)
    {
        ITreeAttribute? t = e?.WatchedAttributes.GetTreeAttribute(ItemTraitsConstants.TreeKey);
        if (t?.GetBool("dragoonBelt", false) ?? false)
        {
            return true;
        }

        if (e?.Player?.InventoryManager is not IPlayerInventoryManager invMan)
        {
            return false;
        }

        IInventory? charInv = invMan.GetOwnInventory(GlobalConstants.characterInvClassName);
        if (charInv is null)
        {
            return false;
        }

        for (int i = 0; i < charInv.Count; i++)
        {
            ItemStack? stack = charInv[i]?.Itemstack;
            if (stack?.Collectible?.Code?.Path == ItemTraitsConstants.CodeBelt)
            {
                return true;
            }
        }

        return false;
    }
}
