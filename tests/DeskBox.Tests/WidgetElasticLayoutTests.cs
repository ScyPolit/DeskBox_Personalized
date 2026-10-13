using DeskBox.Services;

namespace DeskBox.Tests;

public sealed class WidgetElasticLayoutTests
{
    private const double Precision = 6;

    [Fact]
    public void EqualLengths_AreIdentity()
    {
        ElasticAxisPlan plan = WidgetElasticLayout.PlanAxis(1920, 1920.3, [(24, 624)]);

        Assert.True(plan.IsIdentity);
        Assert.Equal((24, 624), plan.MapItem(24, 624, anchorFar: false, double.PositiveInfinity));
    }

    [Fact]
    public void FullGridGrow_KeepsMarginsAndGapsAndStretchesWidgets()
    {
        double column = (1920 - 80) / 3.0;
        (double Start, double End)[] columns =
        [
            (24, 24 + column),
            (24 + column + 16, 24 + 2 * column + 16),
            (24 + 2 * column + 32, 1896)
        ];

        ElasticAxisPlan plan = WidgetElasticLayout.PlanAxis(1920, 3840, columns);
        var mapped = columns.Select(item => plan.MapItem(item.Start, item.End, false, double.PositiveInfinity)).ToArray();

        double expectedColumn = (3840 - 80) / 3.0;
        Assert.Equal(24, mapped[0].Start, Precision);
        Assert.Equal(16, mapped[1].Start - mapped[0].End, Precision);
        Assert.Equal(16, mapped[2].Start - mapped[1].End, Precision);
        Assert.Equal(3840 - 24, mapped[2].End, Precision);
        Assert.All(mapped, item => Assert.Equal(expectedColumn, item.End - item.Start, Precision));
    }

    [Fact]
    public void SparseGrow_ScalesOpenAreaWithWidgetsInsteadOfKeepingItFixed()
    {
        (double Start, double End)[] row = [(1024, 1304), (1320, 1600), (1616, 1896)];

        ElasticAxisPlan plan = WidgetElasticLayout.PlanAxis(1920, 3840, row);
        var mapped = row.Select(item => plan.MapItem(item.Start, item.End, false, double.PositiveInfinity)).ToArray();

        double scale = (3840 - 56) / (840.0 + 1024);
        Assert.Equal(scale, plan.WidgetScale, Precision);
        Assert.Equal(1024 * scale, mapped[0].Start, Precision);
        Assert.Equal(280 * scale, mapped[0].End - mapped[0].Start, Precision);
        Assert.Equal(16, mapped[1].Start - mapped[0].End, Precision);
        Assert.Equal(3840 - 24, mapped[2].End, Precision);
    }

    [Fact]
    public void Shrink_CompressesOpenAreaBeforeTouchingWidgets()
    {
        (double Start, double End)[] row = [(2000, 2560), (2576, 3136), (3152, 3816)];

        ElasticAxisPlan plan = WidgetElasticLayout.PlanAxis(3840, 1920, row);
        var mapped = row.Select(item => plan.MapItem(item.Start, item.End, false, double.PositiveInfinity)).ToArray();

        Assert.Equal(1, plan.WidgetScale, Precision);
        Assert.All(mapped.Zip(row), pair =>
            Assert.Equal(pair.Second.End - pair.Second.Start, pair.First.End - pair.First.Start, Precision));
        Assert.Equal(1920 - 24, mapped[2].End, Precision);
        Assert.Equal(16, mapped[1].Start - mapped[0].End, Precision);
        Assert.True(mapped[0].Start >= WidgetElasticLayout.SpacingThresholdDips);
    }

    [Fact]
    public void Shrink_WithoutOpenArea_ScalesWidgetsAndKeepsSpacing()
    {
        double column = (2560 - 80) / 3.0;
        (double Start, double End)[] columns =
        [
            (24, 24 + column),
            (40 + column, 40 + 2 * column),
            (56 + 2 * column, 2536)
        ];

        ElasticAxisPlan plan = WidgetElasticLayout.PlanAxis(2560, 1920, columns);
        var mapped = columns.Select(item => plan.MapItem(item.Start, item.End, false, double.PositiveInfinity)).ToArray();

        Assert.Equal((1920 - 80) / 3.0, mapped[0].End - mapped[0].Start, Precision);
        Assert.Equal(24, mapped[0].Start, Precision);
        Assert.Equal(16, mapped[1].Start - mapped[0].End, Precision);
        Assert.Equal(1920 - 24, mapped[2].End, Precision);
    }

    [Fact]
    public void Shrink_StopsWidgetsAtMinimumScaleThenCompressesSpacing()
    {
        double column = (3840 - 80) / 3.0;
        (double Start, double End)[] columns =
        [
            (24, 24 + column),
            (40 + column, 40 + 2 * column),
            (56 + 2 * column, 3816)
        ];

        ElasticAxisPlan plan = WidgetElasticLayout.PlanAxis(3840, 1920, columns);
        var mapped = columns.Select(item => plan.MapItem(item.Start, item.End, false, double.PositiveInfinity)).ToArray();

        Assert.Equal(WidgetElasticLayout.MinimumShrinkScale, plan.WidgetScale, Precision);
        Assert.Equal(column * 0.5, mapped[0].End - mapped[0].Start, Precision);
        Assert.Equal(12, mapped[0].Start, Precision);
        Assert.Equal(1920 - 12, mapped[2].End, Precision);
    }

    [Fact]
    public void ShrinkBeyondPreferredFloor_KeepsShrinkingWidgetsInsteadOfOverlapping()
    {
        // 4K full-width 3-column grid onto a 1366 remote display: even the
        // preferred 0.5 floor cannot fit, so covered segments keep shrinking
        // until the plan exactly fills the axis (spec 5.2 step 4).
        double column = (3840 - 80) / 3.0;
        (double Start, double End)[] columns =
        [
            (24, 24 + column),
            (40 + column, 40 + 2 * column),
            (56 + 2 * column, 3816)
        ];

        ElasticAxisPlan plan = WidgetElasticLayout.PlanAxis(3840, 1366, columns);
        var mapped = columns.Select(item => plan.MapItem(item.Start, item.End, false, double.PositiveInfinity)).ToArray();

        Assert.Equal(1366 / 3760.0, plan.WidgetScale, Precision);
        Assert.Equal(0, mapped[0].Start, Precision);
        Assert.Equal(1366, mapped[2].End, Precision);
        Assert.All(mapped, item => Assert.Equal(1366 / 3.0, item.End - item.Start, Precision));
        for (int index = 1; index < mapped.Length; index++)
        {
            Assert.True(mapped[index].Start >= mapped[index - 1].End - 1e-6);
        }
    }

    [Fact]
    public void PlanAxis_MergesOverlappingIntervals_IntoOneCoveredSegment()
    {
        ElasticAxisPlan plan = WidgetElasticLayout.PlanAxis(1000, 2000, [(100, 400), (300, 700)]);

        // Merged covered run is [100,700] (600 DIP), not 800: the two
        // intervals are counted once, so the grow scale is (2000-0)/(600+400).
        (double start, double end) = plan.MapItem(300, 700, false, double.PositiveInfinity);

        Assert.Equal(2, plan.WidgetScale, Precision);
        Assert.Equal(600, start, Precision);
        Assert.Equal(1400, end, Precision);
    }

    [Fact]
    public void FixedContentGrowth_IsCappedAndStaysOnItsAnchoredEdge()
    {
        ElasticAxisPlan plan = WidgetElasticLayout.PlanAxis(1920, 3840, [(1296, 1896)]);

        (double start, double end) = plan.MapItem(1296, 1896, anchorFar: true, WidgetElasticLayout.FixedContentMaxGrowScale);

        Assert.Equal(900, end - start, Precision);
        Assert.Equal(3840 - 24, end, Precision);
    }

    [Theory]
    [InlineData(1920, 3840, false, double.PositiveInfinity)]
    [InlineData(1920, 3840, true, 1.5)]
    [InlineData(3000, 1920, false, double.PositiveInfinity)]
    [InlineData(2560, 1920, true, double.PositiveInfinity)]
    public void UnmapItem_InvertsMapItem(double reference, double target, bool anchorFar, double maxGrow)
    {
        double scale = reference / 1920;
        (double Start, double End)[] items =
        [
            (24 * scale, 624 * scale),
            (640 * scale, 1240 * scale),
            (1296 * scale, 1896 * scale)
        ];
        ElasticAxisPlan plan = WidgetElasticLayout.PlanAxis(reference, target, items);

        foreach ((double start, double end) in items)
        {
            (double mappedStart, double mappedEnd) = plan.MapItem(start, end, anchorFar, maxGrow);
            (double restoredStart, double restoredEnd) = plan.UnmapItem(mappedStart, mappedEnd, anchorFar, maxGrow);
            Assert.Equal(start, restoredStart, Precision);
            Assert.Equal(end, restoredEnd, Precision);
        }
    }

    [Fact]
    public void MappingIsMonotoneAndNeverCreatesOverlaps()
    {
        var random = new Random(20261012);
        for (int round = 0; round < 200; round++)
        {
            double reference = random.Next(800, 4000);
            double target = random.Next(800, 4000);
            List<(double Start, double End)> items = [];
            double cursor = random.Next(0, 80);
            while (cursor < reference - 60)
            {
                double length = random.Next(50, 600);
                if (cursor + length > reference)
                {
                    break;
                }

                items.Add((cursor, cursor + length));
                cursor += length + random.Next(0, 300);
            }

            ElasticAxisPlan plan = WidgetElasticLayout.PlanAxis(reference, target, items);
            var mapped = items.Select(item => plan.MapItem(item.Start, item.End, false, double.PositiveInfinity)).ToList();
            for (int index = 1; index < mapped.Count; index++)
            {
                Assert.True(mapped[index].Start >= mapped[index - 1].End - 1e-6);
            }

            if (plan.WidgetScale > WidgetElasticLayout.MinimumShrinkScale + 1e-9)
            {
                for (double point = 0; point <= reference; point += reference / 37)
                {
                    Assert.Equal(point, plan.Unmap(plan.Map(point)), 4);
                }
            }
            else
            {
                // Below the preferred floor some blank segments collapse to
                // zero target length (the map is no longer injective there —
                // an item START sitting on a collapsed gap snaps to the gap
                // boundary); round-trip only at item interiors, which always
                // live inside covered runs and stay injective.
                foreach ((double start, double end) in items)
                {
                    double length = end - start;
                    foreach (double point in new[]
                             {
                                 start + length * 0.25,
                                 start + length * 0.5,
                                 start + length * 0.75,
                             })
                    {
                        Assert.Equal(point, plan.Unmap(plan.Map(point)), 4);
                    }
                }
            }
        }
    }

    [Fact]
    public void AdaptiveMinimumLength_FollowsTheNarrowestItemBelowThePreferredFloor()
    {
        Assert.Equal(36.49, WidgetElasticLayout.AdaptiveMinimumLength(50, 1366 / 3744.0, 100), 2);
        Assert.Equal(50, WidgetElasticLayout.AdaptiveMinimumLength(50, 1, 600), 6);
        // Positive infinity (no items) or plain scales keep the preferred floor.
        Assert.Equal(50, WidgetElasticLayout.AdaptiveMinimumLength(50, double.PositiveInfinity, 600), 6);
        // The fixed-content grow cap keeps the floor from following a huge scale.
        Assert.Equal(50, WidgetElasticLayout.AdaptiveMinimumLength(50, 3, 100), 6);
        Assert.Equal(45, WidgetElasticLayout.AdaptiveMinimumLength(50, 3, 30), 6);
    }

    [Fact]
    public void DisplayPlan_ExposesAdaptiveMinimumFromNarrowestItem()
    {
        ElasticDisplayPlan plan = ElasticDisplayPlan.Create(
            3840, 2080, 1366, 728,
            [
                new ElasticRect(24, 40, 24 + 1772, 540),
                new ElasticRect(1812, 40, 1912, 540),
                new ElasticRect(1928, 40, 2028, 540),
                new ElasticRect(2044, 40, 2044 + 1772, 540),
            ]);

        // X shrinks below the floor (ws = 1366/3744): the floor follows the
        // narrowest item so the projection clamp never inflates it.
        Assert.Equal(100 * 1366 / 3744.0, plan.AdaptiveMinimumWidth, 4);
        // Y only compresses open space (no widget shrink): floor stays preferred.
        Assert.Equal(50, plan.AdaptiveMinimumHeight, 6);
    }
}
