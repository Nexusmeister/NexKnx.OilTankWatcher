using NexKnx.OilTankWatcher.Processing;

namespace NexKnx.OilTankWatcher.Tests;

public class MedianSmootherTests
{
    [Fact]
    public void Add_SingleValue_ReturnsThatValue()
    {
        var smoother = new MedianSmoother(5);

        var result = smoother.Add(42);

        result.ShouldBe(42);
    }

    [Fact]
    public void Add_FullWindow_ReturnsMedianOfLastFiveValues()
    {
        var smoother = new MedianSmoother(5);

        smoother.Add(10);
        smoother.Add(90);
        smoother.Add(20);
        smoother.Add(80);
        var result = smoother.Add(30);

        // Window: 10, 90, 20, 80, 30 -> sorted: 10, 20, 30, 80, 90 -> median 30
        result.ShouldBe(30);
    }

    [Fact]
    public void Add_IsRobustAgainstSingleOutlierWithinWindow()
    {
        var smoother = new MedianSmoother(5);

        smoother.Add(50);
        smoother.Add(51);
        smoother.Add(49);
        smoother.Add(50);
        var result = smoother.Add(500); // Outlier that passed the plausibility check, e.g. in an edge case

        // Window: 50, 51, 49, 50, 500 -> sorted: 49, 50, 50, 51, 500 -> median 50
        result.ShouldBe(50);
    }

    [Fact]
    public void Add_SlidingWindow_DropsOldestValueBeyondWindowSize()
    {
        var smoother = new MedianSmoother(3);

        smoother.Add(10);
        smoother.Add(20);
        smoother.Add(30); // Window: 10,20,30 -> median 20
        var result = smoother.Add(100); // Window: 20,30,100 -> median 30 (10 has dropped out)

        result.ShouldBe(30);
    }

    [Fact]
    public void Add_EvenWindowSize_ReturnsAverageOfTwoMiddleValues()
    {
        var smoother = new MedianSmoother(4);

        smoother.Add(10);
        smoother.Add(20);
        smoother.Add(30);
        var result = smoother.Add(40);

        // sorted: 10,20,30,40 -> median (20+30)/2 = 25
        result.ShouldBe(25);
    }

    [Fact]
    public void Constructor_ThrowsForNonPositiveWindowSize()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new MedianSmoother(0));
        Should.Throw<ArgumentOutOfRangeException>(() => new MedianSmoother(-3));
    }
}
