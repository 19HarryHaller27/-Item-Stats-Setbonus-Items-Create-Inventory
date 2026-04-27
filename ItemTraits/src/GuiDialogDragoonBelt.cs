using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ItemTraits;

internal sealed class GuiDialogDragoonBelt : GuiDialog
{
    private const string DynamicTextKey = "itemtraits-dragoon-text";
    private readonly ICoreClientAPI clientApi;
    private readonly IInventory beltInventory;
    private string currentText;

    public GuiDialogDragoonBelt(ICoreClientAPI capi, IInventory beltInventory, string initialText) : base(capi)
    {
        clientApi = capi;
        this.beltInventory = beltInventory;
        currentText = initialText;
        ComposeDialog();
    }

    public override string ToggleKeyCombinationCode => string.Empty;

    public override bool OnEscapePressed()
    {
        TryClose();
        return true;
    }

    public void Refresh(string text)
    {
        if (text == currentText)
        {
            return;
        }

        currentText = text;
        SingleComposer?.GetDynamicText(DynamicTextKey)?.SetNewText(text, false, true, false);
    }

    private void ComposeDialog()
    {
        CairoFont font = CairoFont.WhiteSmallText().WithLineHeightMultiplier(1.2);
        ElementBounds textBounds = ElementBounds.Fixed(0, 24, 430, 188);
        ElementBounds slotGridBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, textBounds.fixedY + textBounds.fixedHeight + 8, 3, 1);

        double pad = GuiStyle.ElementToDialogPadding;
        ElementBounds bg = ElementBounds.Fill.WithFixedPadding(pad);
        bg.BothSizing = ElementSizing.FitToChildren;
        textBounds = textBounds.WithParent(bg);
        slotGridBounds = slotGridBounds.WithParent(bg);
        _ = bg.WithChildren(textBounds, slotGridBounds);

        ElementBounds dialog = ElementStdBounds.AutosizedMainDialog
            .WithAlignment(EnumDialogArea.None)
            .WithFixedPosition(20, 120);

        SingleComposer = clientApi.Gui
            .CreateCompo("itemtraits-dragoon-belt-dialog", dialog)
            .AddShadedDialogBG(bg, true, 0, 0.5f)
            .AddDialogTitleBar(Lang.Get("itemtraits:gui-title"), () => { TryClose(); })
            .BeginChildElements(bg)
            .AddDynamicText(currentText, font, textBounds, DynamicTextKey)
            .AddItemSlotGrid(beltInventory, OnSendPacket, 3, slotGridBounds, "dragoonbelt-slots")
            .EndChildElements()
            .Compose();
    }

    private void OnSendPacket(object packet)
    {
        clientApi.Network.SendPacketClient(packet);
    }
}
