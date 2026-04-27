using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace ItemTraits;

internal static class EntityHealthBonus
{
    public static void SetFlatMaxHpBonus(EntityAgent entity, string key, float bonusHp)
    {
        if (entity is null || bonusHp <= 0f) return;

        object? beh = entity.GetBehavior("health");
        if (beh is null) return;

        MethodInfo? m = beh.GetType().GetMethod("SetMaxHealthModifiers", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string), typeof(float) }, null);
        m?.Invoke(beh, new object[] { key, bonusHp });
    }

    public static void ClearFlatMaxHpBonus(EntityAgent entity, string key)
    {
        object? beh = entity?.GetBehavior("health");
        if (beh is null) return;

        MethodInfo? m = beh.GetType().GetMethod("SetMaxHealthModifiers", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string), typeof(float) }, null);
        m?.Invoke(beh, new object[] { key, 0f });
    }
}
