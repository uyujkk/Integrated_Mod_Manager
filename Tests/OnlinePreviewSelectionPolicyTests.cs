using IntegratedModManager.Core;

namespace IntegratedModManager.Core.Tests;

public sealed class OnlinePreviewSelectionPolicyTests
{
    [Fact]
    public void SelectBest_PrefersClearScreenshotOverListThumbnail()
    {
        OnlinePreviewCandidate thumbnail = Candidate("thumbnail", 530, 298, fallback: true, order: 0);
        OnlinePreviewCandidate screenshot = Candidate("screenshot", 1280, 720, fallback: false, order: 1);

        OnlinePreviewCandidate? selected = OnlinePreviewSelectionPolicy.SelectBest([thumbnail, screenshot]);

        Assert.Same(screenshot, selected);
    }

    [Fact]
    public void SelectBest_UsesHighestUsefulResolutionAmongScreenshots()
    {
        OnlinePreviewCandidate smaller = Candidate("smaller", 800, 450, fallback: false, order: 0);
        OnlinePreviewCandidate larger = Candidate("larger", 1920, 1080, fallback: false, order: 1);

        OnlinePreviewCandidate? selected = OnlinePreviewSelectionPolicy.SelectBest([smaller, larger]);

        Assert.Same(larger, selected);
    }

    [Fact]
    public void SelectBest_PrefersClearImageOverExtremeBanner()
    {
        OnlinePreviewCandidate banner = Candidate("banner", 2400, 300, fallback: false, order: 0);
        OnlinePreviewCandidate screenshot = Candidate("screenshot", 800, 450, fallback: false, order: 1);

        OnlinePreviewCandidate? selected = OnlinePreviewSelectionPolicy.SelectBest([banner, screenshot]);

        Assert.Same(screenshot, selected);
    }

    [Fact]
    public void SelectBest_FallsBackToValidThumbnailWhenNoScreenshotLoads()
    {
        OnlinePreviewCandidate thumbnail = Candidate("thumbnail", 530, 298, fallback: true, order: 0);

        OnlinePreviewCandidate? selected = OnlinePreviewSelectionPolicy.SelectBest([thumbnail]);

        Assert.Same(thumbnail, selected);
    }

    [Fact]
    public void SelectBest_IgnoresCandidatesWithoutDecodedDimensions()
    {
        OnlinePreviewCandidate invalid = Candidate("invalid", 0, 0, fallback: false, order: 0);
        OnlinePreviewCandidate valid = Candidate("valid", 800, 450, fallback: false, order: 1);

        OnlinePreviewCandidate? selected = OnlinePreviewSelectionPolicy.SelectBest([invalid, valid]);

        Assert.Same(valid, selected);
    }

    private static OnlinePreviewCandidate Candidate(
        string url,
        int width,
        int height,
        bool fallback,
        int order)
    {
        return new OnlinePreviewCandidate
        {
            Url = url,
            PixelWidth = width,
            PixelHeight = height,
            FileSizeBytes = Math.Max(1, width * height / 5),
            IsFallbackThumbnail = fallback,
            SourceOrder = order
        };
    }
}
