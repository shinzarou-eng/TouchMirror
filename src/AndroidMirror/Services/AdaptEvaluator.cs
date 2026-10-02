namespace TouchMirror.Services;

public sealed class AdaptEvaluator
{
    public const int MaxBitRate = 40_000_000;
    public static int MinBitRate => Tiers[0];
    private static readonly int[] Tiers =
        { 1_500_000, 3_000_000, 5_000_000, 8_000_000, 12_000_000,
          16_000_000, 20_000_000, 24_000_000, 32_000_000, 40_000_000 };

    private double _lastLag = double.NaN;
    private int _goodStreak;
    private int _tier;
    private int _floorTier;
    private readonly int _ceilingTier;

    public AdaptEvaluator(int startBitRate, int ceilingBitRate, int minBitRate = 0)
    {
        _ceilingTier = CeilingIndex(ceilingBitRate);
        _floorTier = Math.Min(CeilingIndex(minBitRate), _ceilingTier);
        _tier = Math.Min(StartIndex(startBitRate), _ceilingTier);
        PeakBitRate = Current;
    }

    public static int FloorForSize(int maxDim) => maxDim switch
    {
        >= 2560 => 8_000_000,
        >= 1440 => 5_000_000,
        _ => 0,
    };

    public int? SetFloor(int minBitRate)
    {
        _floorTier = Math.Min(CeilingIndex(minBitRate), _ceilingTier);
        if (_tier >= _floorTier)
            return null;
        _tier = _floorTier;
        return Current;
    }

    public int Current => Tiers[_tier];
    public int PeakBitRate { get; private set; }
    public int Moves { get; private set; }
    public int StableTicks { get; private set; }

    private static int StartIndex(int rate)
    {
        for (var i = 0; i < Tiers.Length; i++)
            if (Tiers[i] >= rate)
                return i;
        return Tiers.Length - 1;
    }

    private static int CeilingIndex(int rate)
    {
        var i = 0;
        while (i + 1 < Tiers.Length && Tiers[i + 1] <= rate)
            i++;
        return i;
    }

    public void Reset()
    {
        _lastLag = double.NaN;
        _goodStreak = 0;
    }

    public int? Evaluate(double lagEmaMs, bool videoHidden)
    {
        if (videoHidden)
            return null;
        var growth = double.IsNaN(_lastLag) ? 0 : lagEmaMs - _lastLag;
        _lastLag = lagEmaMs;
        var moved = false;
        if (growth > 60 || lagEmaMs > 400)
        {
            _goodStreak = 0;
            if (_tier > _floorTier)
            {
                _tier--;
                moved = true;
            }
        }
        else if (lagEmaMs < 150)
        {
            if (++_goodStreak >= 5 && _tier < _ceilingTier)
            {
                _goodStreak = 0;
                _tier++;
                moved = true;
            }
        }
        else
            _goodStreak = 0;
        if (moved)
        {
            Moves++;
            StableTicks = 0;
            if (Current > PeakBitRate)
                PeakBitRate = Current;
            return Current;
        }
        StableTicks++;
        return null;
    }
}
