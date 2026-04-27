using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ItemTraits;

internal sealed class DragoonBeltInventory : InventoryBasePlayer
{
    private ItemSlot[] slots;

    public DragoonBeltInventory(string playerUid, ICoreAPI api)
        : base(ItemTraitsConstants.BeltInventoryClassName, playerUid, api)
    {
        slots = GenEmptySlots(ItemTraitsConstants.BeltSlots);
    }

    public override int Count => slots.Length;

    public override ItemSlot this[int slotId]
    {
        get
        {
            if ((uint)slotId >= (uint)slots.Length) return null!;
            return slots[slotId];
        }
        set
        {
            if ((uint)slotId >= (uint)slots.Length || value is null) return;
            slots[slotId] = value;
            MarkSlotDirty(slotId);
        }
    }

    public bool HasAnyItems()
    {
        for (int i = 0; i < slots.Length; i++)
        {
            if (!slots[i].Empty) return true;
        }

        return false;
    }

    public override void FromTreeAttributes(ITreeAttribute tree)
    {
        slots = SlotsFromTreeAttributes(tree, slots);
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        SlotsToTreeAttributes(slots, tree);
    }
}
