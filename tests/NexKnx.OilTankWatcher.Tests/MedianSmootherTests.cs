using NexKnx.OilTankWatcher.Processing;

namespace NexKnx.OilTankWatcher.Tests;

public class MedianSmootherTests
{
    [Fact]
    public void Add_SingleValue_ReturnsThatValue()
    {
        var smoother = new MedianSmoother(5);

        var result = smoother.Add(42);

        Assert.Equal(42, result);
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

        // Fenster: 10, 90, 20, 80, 30 -> sortiert: 10, 20, 30, 80, 90 -> Median 30
        Assert.Equal(30, result);
    }

    [Fact]
    public void Add_IsRobustAgainstSingleOutlierWithinWindow()
    {
        var smoother = new MedianSmoother(5);

        smoother.Add(50);
        smoother.Add(51);
        smoother.Add(49);
        smoother.Add(50);
        var result = smoother.Add(500); // Ausreißer, der die Plausibilitätsprüfung z. B. im Grenzfall passiert hat

        // Fenster: 50, 51, 49, 50, 500 -> sortiert: 49, 50, 50, 51, 500 -> Median 50
        Assert.Equal(50, result);
    }

    [Fact]
    public void Add_SlidingWindow_DropsOldestValueBeyondWindowSize()
    {
        var smoother = new MedianSmoother(3);

        smoother.Add(10);
        smoother.Add(20);
        smoother.Add(30); // Fenster: 10,20,30 -> Median 20
        var result = smoother.Add(100); // Fenster: 20,30,100 -> Median 30 (10 ist herausgefallen)

        Assert.Equal(30, result);
    }

    [Fact]
    public void Add_EvenWindowSize_ReturnsAverageOfTwoMiddleValues()
    {
        var smoother = new MedianSmoother(4);

        smoother.Add(10);
        smoother.Add(20);
        smoother.Add(30);
        var result = smoother.Add(40);

        // sortiert: 10,20,30,40 -> Median (20+30)/2 = 25
        Assert.Equal(25, result);
    }

    [Fact]
    public void Constructor_ThrowsForNonPositiveWindowSize()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MedianSmoother(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MedianSmoother(-3));
    }
}
