using Content.Shared.Actions;

namespace Content.Shared._WF.Modsuits;

/// <summary>Small compatibility helpers for action lookups used by the modsuit systems.</summary>
public static class ModSuitActionExtensions
{
    /// <summary>Returns an action component as an entity handle, or null when it is missing.</summary>
    public static Entity<BaseActionComponent>? GetAction(this SharedActionsSystem actions, EntityUid? uid, bool logError = false)
    {
        if (uid is not { } action || !actions.TryGetActionData(action, out var component, logError))
            return null;

        return (action, component);
    }
}
