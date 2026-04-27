using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.API.MathTools;

namespace ItemTraits;

public class ItemTraitsServerSystem : ModSystem
{
    private readonly HashSet<string> grantedFlightByPlayer = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> beltHomeSlotByPlayer = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> nextUnequipWarnMs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> nextSwordBleedTickMs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> lastEquipMaskByPlayer = new(StringComparer.Ordinal);
    private readonly HashSet<string> serverOpenedBeltInventoryByPlayer = new(StringComparer.Ordinal);

    private ICoreServerAPI? sapi;
    private long tickId = -1;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

    public override void StartServerSide(ICoreServerAPI api)
    {
        sapi = api;
        tickId = api.Event.RegisterGameTickListener(OnServerTick, 300, 0);
    }

    public override void Dispose()
    {
        if (sapi is not null && tickId != -1)
        {
            sapi.Event.UnregisterGameTickListener(tickId);
            tickId = -1;
        }

        sapi = null;
        grantedFlightByPlayer.Clear();
        beltHomeSlotByPlayer.Clear();
        nextUnequipWarnMs.Clear();
        nextSwordBleedTickMs.Clear();
        lastEquipMaskByPlayer.Clear();
        serverOpenedBeltInventoryByPlayer.Clear();
    }

    private void OnServerTick(float _dt)
    {
        if (sapi is null)
        {
            return;
        }

        foreach (IPlayer pl in sapi.Server.Players)
        {
            if (pl is not IServerPlayer sp || sp.Entity is null)
            {
                continue;
            }

            ProcessPlayer(sp);
            ApplySwordBleedIfNeeded(sp);
        }
    }

    private void ProcessPlayer(IServerPlayer sp)
    {
        EntityPlayer ent = sp.Entity;
        IInventory? charInv = sp.InventoryManager.GetOwnInventory(GlobalConstants.characterInvClassName);
        if (charInv is null)
        {
            ClearAll(ent, sp.PlayerUID);
            return;
        }

        DragoonBeltInventory beltInv = EnsureBeltInventory(sp);

        ItemStack? shoes = null;
        ItemStack? belt = null;
        ItemStack? shirt = null;
        ItemStack? helm = null;
        int beltSlot = -1;

        for (int i = 0; i < charInv.Count; i++)
        {
            ItemStack? st = charInv[i].Itemstack;
            string? p = st?.Collectible?.Code?.Path;
            if (p is null) continue;

            if (p == ItemTraitsConstants.CodeShoes) shoes = st;
            else if (p == ItemTraitsConstants.CodeBelt)
            {
                belt = st;
                beltSlot = i;
            }
            else if (p == ItemTraitsConstants.CodeShirt) shirt = st;
                else if (p == ItemTraitsConstants.CodeHelm) helm = st;
        }

        if (beltSlot >= 0)
        {
            beltHomeSlotByPlayer[sp.PlayerUID] = beltSlot;
        }

        bool beltWorn = belt is not null;

        if (!beltWorn && beltInv.HasAnyItems())
        {
            beltWorn = TryForceReequipBelt(sp, charInv);

            long now = sapi!.World.ElapsedMilliseconds;
            if (nextUnequipWarnMs.GetValueOrDefault(sp.PlayerUID, 0) <= now)
            {
                sp.SendMessage(GlobalConstants.GeneralChatGroup, "Dragoon Belt cannot be unequipped while Dragoon slots contain items.", EnumChatType.Notification);
                nextUnequipWarnMs[sp.PlayerUID] = now + 3000;
            }
        }

        bool shoesWorn = shoes is not null;
        bool helmWorn = helm is not null;
        bool shirtWorn = shirt is not null;
        SendEquipPrompts(sp, shoesWorn, beltWorn, helmWorn, shirtWorn);

        bool shoesActive = IsFunctional(shoes, requireDurability: true);
        bool beltSlotsActive = beltWorn;
        bool shirtActive = IsFunctional(shirt, requireDurability: true);
        bool helmActive = IsFunctional(helm, requireDurability: true);
        bool swordActive = IsHeldSwordFunctional(sp.Entity);

        SyncServerBeltInventoryOpenState(sp, beltInv, beltSlotsActive);

        ApplyShoes(ent, shoesActive);
        ApplyHelm(ent, helmActive);
        ApplyShirt(ent, shirtActive);

        bool fullSetActive = shoesActive && beltSlotsActive && shirtActive && helmActive && swordActive;
        ApplyFlight(sp, fullSetActive);

        SetStatusTree(ent, shoesActive, beltSlotsActive, helmActive, shirtActive, swordActive, fullSetActive);
        SaveBeltInventoryToEntity(sp, beltInv);
    }

    private DragoonBeltInventory EnsureBeltInventory(IServerPlayer sp)
    {
        IInventory? inv = sp.InventoryManager.GetOwnInventory(ItemTraitsConstants.BeltInventoryClassName);
        if (inv is DragoonBeltInventory belt)
        {
            return belt;
        }

        var created = new DragoonBeltInventory(sp.PlayerUID, sapi!);
        LoadBeltInventoryFromEntity(sp, created);
        sp.InventoryManager.Inventories[created.InventoryID] = created;
        return created;
    }

    private static void LoadBeltInventoryFromEntity(IServerPlayer sp, DragoonBeltInventory inv)
    {
        ITreeAttribute? saved = sp.Entity.WatchedAttributes.GetTreeAttribute(ItemTraitsConstants.BeltInventoryTreeKey);
        if (saved is null)
        {
            return;
        }

        inv.FromTreeAttributes(saved);
    }

    private static void SaveBeltInventoryToEntity(IServerPlayer sp, DragoonBeltInventory inv)
    {
        ITreeAttribute t = new TreeAttribute();
        inv.ToTreeAttributes(t);
        sp.Entity.WatchedAttributes[ItemTraitsConstants.BeltInventoryTreeKey] = t;
        sp.Entity.WatchedAttributes.MarkPathDirty(ItemTraitsConstants.BeltInventoryTreeKey);
    }

    private static bool IsSamePath(ItemStack? st, string path) => st?.Collectible?.Code?.Path == path;

    private bool TryForceReequipBelt(IServerPlayer sp, IInventory charInv)
    {
        if (!beltHomeSlotByPlayer.TryGetValue(sp.PlayerUID, out int homeSlotId))
        {
            return false;
        }

        if (homeSlotId < 0 || homeSlotId >= charInv.Count)
        {
            return false;
        }

        ItemSlot home = charInv[homeSlotId];
        if (IsSamePath(home.Itemstack, ItemTraitsConstants.CodeBelt))
        {
            return true;
        }

        ItemSlot? beltSource = null;

        foreach (InventoryBase ib in sp.InventoryManager.InventoriesOrdered)
        {
            // Never treat Dragoon belt storage slots as a source belt location.
            if (ib is DragoonBeltInventory)
            {
                continue;
            }

            for (int i = 0; i < ib.Count; i++)
            {
                ItemSlot src = ib[i];
                if (!IsSamePath(src.Itemstack, ItemTraitsConstants.CodeBelt))
                {
                    continue;
                }

                beltSource = src;
                break;
            }

            if (beltSource is not null) break;
        }

        if (beltSource?.Itemstack is null)
        {
            return false;
        }

        if (home.Empty)
        {
            home.Itemstack = beltSource.Itemstack;
            beltSource.Itemstack = null;
            home.MarkDirty();
            beltSource.MarkDirty();
            return true;
        }

        // Swap to force belt back into waist slot while preserving both stacks.
        ItemStack? existingHome = home.Itemstack;
        home.Itemstack = beltSource.Itemstack;
        beltSource.Itemstack = existingHome;
        home.MarkDirty();
        beltSource.MarkDirty();
        return true;
    }

    private void SyncServerBeltInventoryOpenState(IServerPlayer sp, DragoonBeltInventory beltInv, bool shouldBeOpen)
    {
        bool isOpen = serverOpenedBeltInventoryByPlayer.Contains(sp.PlayerUID);

        if (shouldBeOpen && !isOpen)
        {
            sp.InventoryManager.OpenInventory(beltInv);
            serverOpenedBeltInventoryByPlayer.Add(sp.PlayerUID);
            return;
        }

        if (!shouldBeOpen && isOpen)
        {
            sp.InventoryManager.CloseInventoryAndSync(beltInv);
            serverOpenedBeltInventoryByPlayer.Remove(sp.PlayerUID);
        }
    }

    private void ApplyFlight(IServerPlayer sp, bool fullSetActive)
    {
        bool survival = sp.WorldData.CurrentGameMode == EnumGameMode.Survival;
        bool had = grantedFlightByPlayer.Contains(sp.PlayerUID);

        if (survival && fullSetActive)
        {
            if (!had || !sp.WorldData.FreeMove)
            {
                sp.WorldData.FreeMove = true;
                sp.WorldData.NoClip = false;
                sp.BroadcastPlayerData(false);
            }

            grantedFlightByPlayer.Add(sp.PlayerUID);
            return;
        }

        if (!had)
        {
            return;
        }

        sp.WorldData.FreeMove = false;
        sp.WorldData.NoClip = false;
        sp.BroadcastPlayerData(false);
        grantedFlightByPlayer.Remove(sp.PlayerUID);
    }

    private void ApplySwordBleedIfNeeded(IServerPlayer sp)
    {
        if (sapi is null || sp.Entity is null)
        {
            return;
        }

        ItemStack? held = sp.Entity.RightHandItemSlot?.Itemstack;
        if (held?.Collectible?.Code?.Path != ItemTraitsConstants.CodeSword)
        {
            nextSwordBleedTickMs.Remove(sp.PlayerUID);
            return;
        }

        long now = sapi.World.ElapsedMilliseconds;
        long next = nextSwordBleedTickMs.GetValueOrDefault(sp.PlayerUID, 0);
        if (now < next)
        {
            return;
        }

        var src = new DamageSource
        {
            Source = EnumDamageSource.Internal,
            Type = EnumDamageType.PiercingAttack,
            SourceEntity = sp.Entity,
            DamageTier = 0,
            KnockbackStrength = 0f
        };

        sp.Entity.ReceiveDamage(src, ItemTraitsConstants.SwordBleedDamagePerTick);
        nextSwordBleedTickMs[sp.PlayerUID] = now + ItemTraitsConstants.SwordBleedTickMs;
    }

    private static void ApplyShoes(EntityPlayer e, bool on)
    {
        if (!on)
        {
            e.Stats.Remove("walkspeed", ItemTraitsConstants.StatLayerShoes);
            e.Stats.Remove("sprintSpeed", ItemTraitsConstants.StatLayerShoes);
            return;
        }

        e.Stats.Set("walkspeed", ItemTraitsConstants.StatLayerShoes, ItemTraitsConstants.ShoesMoveAdd, persistent: false);
        e.Stats.Set("sprintSpeed", ItemTraitsConstants.StatLayerShoes, ItemTraitsConstants.ShoesMoveAdd, persistent: false);
    }

    private static void ApplyHelm(EntityPlayer e, bool on)
    {
        if (!on)
        {
            e.Stats.Remove("healingeffectivness", ItemTraitsConstants.StatLayerHelm);
            return;
        }

        e.Stats.Set("healingeffectivness", ItemTraitsConstants.StatLayerHelm, ItemTraitsConstants.HelmHealingEffectAdd, persistent: false);
    }

    private static void ApplyShirt(EntityPlayer e, bool on)
    {
        if (!on)
        {
            EntityHealthBonus.ClearFlatMaxHpBonus(e, ItemTraitsConstants.HpBonusKey);
            return;
        }

        EntityHealthBonus.SetFlatMaxHpBonus(e, ItemTraitsConstants.HpBonusKey, ItemTraitsConstants.ShirtMaxHpAdd);
    }

    private static bool IsFunctional(ItemStack? stack, bool requireDurability)
    {
        if (stack?.Collectible is null)
        {
            return false;
        }

        if (!requireDurability)
        {
            return true;
        }

        int max = stack.Collectible.GetMaxDurability(stack);
        if (max <= 0)
        {
            return true;
        }

        int rem = stack.Collectible.GetRemainingDurability(stack);
        return rem > 0;
    }

    private void SendEquipPrompts(IServerPlayer sp, bool shoesWorn, bool beltWorn, bool helmWorn, bool shirtWorn)
    {
        int currentMask = 0;
        if (shoesWorn) currentMask |= 1 << 0;
        if (beltWorn) currentMask |= 1 << 1;
        if (helmWorn) currentMask |= 1 << 2;
        if (shirtWorn) currentMask |= 1 << 3;

        int prevMask = lastEquipMaskByPlayer.GetValueOrDefault(sp.PlayerUID, 0);
        if (currentMask == prevMask)
        {
            return;
        }

        EmitEquipPromptIfNew(sp, prevMask, currentMask, 1 << 0, "As you lace on the Dragoon shoes, your stride quickens with draconic purpose.");
        EmitEquipPromptIfNew(sp, prevMask, currentMask, 1 << 1, "As you fasten the Dragoon belt, you feel hidden space unfurl at your waist.");
        EmitEquipPromptIfNew(sp, prevMask, currentMask, 1 << 2, "As you don the Dragoon helm, a stern dragon-will washes through your mind.");
        EmitEquipPromptIfNew(sp, prevMask, currentMask, 1 << 3, "As you pull on the Dragoon shirt, a steady dragon-heart beats within your chest.");

        lastEquipMaskByPlayer[sp.PlayerUID] = currentMask;
    }

    private static void EmitEquipPromptIfNew(IServerPlayer sp, int prevMask, int currentMask, int bit, string message)
    {
        bool had = (prevMask & bit) != 0;
        bool has = (currentMask & bit) != 0;
        if (!had && has)
        {
            sp.SendMessage(GlobalConstants.GeneralChatGroup, message, EnumChatType.Notification);
        }
    }

    private static bool IsHeldSwordFunctional(EntityPlayer e)
    {
        ItemStack? held = e.RightHandItemSlot?.Itemstack;
        if (held?.Collectible?.Code?.Path != ItemTraitsConstants.CodeSword)
        {
            return false;
        }

        return IsFunctional(held, requireDurability: true);
    }

    private static void SetStatusTree(EntityPlayer e, bool shoes, bool belt, bool helm, bool shirt, bool sword, bool fullSet)
    {
        ITreeAttribute t = e.WatchedAttributes.GetOrAddTreeAttribute(ItemTraitsConstants.TreeKey);
        t.SetBool("dragoonShoes", shoes);
        t.SetBool("dragoonBelt", belt);
        t.SetBool("dragoonHelm", helm);
        t.SetBool("dragoonShirt", shirt);
        t.SetBool("dragoonSword", sword);
        t.SetBool("fullSet", fullSet);
        e.WatchedAttributes.MarkPathDirty(ItemTraitsConstants.TreeKey);
    }

    private void ClearAll(EntityPlayer e, string playerUid)
    {
        ApplyShoes(e, false);
        ApplyHelm(e, false);
        ApplyShirt(e, false);

        grantedFlightByPlayer.Remove(playerUid);
        lastEquipMaskByPlayer.Remove(playerUid);
        serverOpenedBeltInventoryByPlayer.Remove(playerUid);
        SetStatusTree(e, false, false, false, false, false, false);
    }
}
