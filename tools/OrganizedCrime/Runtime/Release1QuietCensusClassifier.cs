namespace OrganizedCrime.Runtime;

/// <summary>
/// What the whole census reads as this pass: NotReady when any drop or the hold room could not be
/// read, or the observed counts do not match the frozen expectation; Present when the frozen product
/// is found anywhere, in any packaging, in any quantity; Clear only when every expected drop and
/// every expected closet read Ready and none of them carries the product at all. A snapshot list with
/// the wrong count is never partially trusted: a world mid layout change holds rather than reporting a
/// false Clear.
/// </summary>
public enum Release1QuietCensusObservation
{
    NotReady,
    Clear,
    Present
}
