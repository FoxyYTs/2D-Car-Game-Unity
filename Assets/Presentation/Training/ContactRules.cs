using UnityEngine;

// The game's rules for two cars touching, checked geometrically so cars that ignore each other in the physics
// (agents of other matches on the same layer) can still be judged: my front bumper (circle) on your body (box),
// I rammed you; bumper on bumper or body on body, or both at once, a tie.
public static class ContactRules
{
    public enum Contact
    {
        None,
        FirstRams,
        SecondRams,
        Tie
    }

    // Colliders closer than this count as touching, like the physics contact offset.
    private const float CONTACT_DISTANCE = 0.02f;

    // Call Physics2D.SyncTransforms first if the cars were moved since the last physics step.
    public static Contact Resolve(Component first, Component second)
    {
        var firstBumper = first.GetComponent<CircleCollider2D>();
        var firstBody = first.GetComponent<BoxCollider2D>();
        var secondBumper = second.GetComponent<CircleCollider2D>();
        var secondBody = second.GetComponent<BoxCollider2D>();

        bool firstRams = Touching(firstBumper, secondBody);
        bool secondRams = Touching(secondBumper, firstBody);
        bool bumpers = Touching(firstBumper, secondBumper);
        bool bodies = Touching(firstBody, secondBody);

        if (!(firstRams || secondRams || bumpers || bodies))
            return Contact.None;
        if (firstRams && !secondRams && !bumpers && !bodies)
            return Contact.FirstRams;
        if (secondRams && !firstRams && !bumpers && !bodies)
            return Contact.SecondRams;
        return Contact.Tie;
    }

    private static bool Touching(Collider2D a, Collider2D b)
    {
        var distance = a.Distance(b);
        return distance.isValid && distance.distance <= CONTACT_DISTANCE;
    }
}
