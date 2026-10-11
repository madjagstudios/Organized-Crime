namespace OrganizedCrime.Model;

/// <summary>
/// A Chapter 3 reply button the player can tap. Records, so the projector can
/// match a tapped button to the live state and tests can assert exact options.
/// </summary>
public abstract record Chapter3PresentationCommand;

public sealed record Chapter3AcceptCommand : Chapter3PresentationCommand;

public sealed record Chapter3DeclineCommand : Chapter3PresentationCommand;

public sealed record Chapter3BuyTimeCommand : Chapter3PresentationCommand;

public sealed record Chapter3BuyWarehouseCommand : Chapter3PresentationCommand;

/// <summary>
/// OC-117: buys the currently open warehouse expansion tier. The tier is
/// derived from live state at tap time, so the command carries none.
/// </summary>
public sealed record Chapter3BuyWarehouseCapacityCommand : Chapter3PresentationCommand;

public sealed record Chapter3PayMakeGoodCommand(int MakeGoodId) : Chapter3PresentationCommand;

public sealed record Chapter3PayCutCommand(int WeekIndex) : Chapter3PresentationCommand;
