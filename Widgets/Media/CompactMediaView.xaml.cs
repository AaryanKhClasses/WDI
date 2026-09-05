using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WDI.Widgets.Media;

public sealed partial class CompactMediaView : UserControl
{
    private MediaService? _mediaService;
    private MediaInfo? _mediaInfo;

    public CompactMediaView()
    {
        InitializeComponent();

        PreviousButton.Click += PreviousButton_Click;
        PlayPauseButton.Click += PlayPauseButton_Click;
        NextButton.Click += NextButton_Click;
    }

    public void Initialize(MediaService mediaService)
    {
        _mediaService = mediaService;
    }

    public void Update(MediaInfo? mediaInfo)
    {
        _mediaInfo = mediaInfo;
        if (mediaInfo is null)
        {
            TitleText.Text = "No Media";
            ArtistText.Text = "Unknown Artist";
            PlayPauseButton.Content = "▶️";
            return;
        }

        TitleText.Text = mediaInfo.Title;
        ArtistText.Text = mediaInfo.Artist;
        PlayPauseButton.Content = mediaInfo.IsPlaying ? "⏸️" : "▶️";
    }

    private async void PreviousButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mediaService is null) return;
        await _mediaService.SkipPreviousAsync();
        var mediaInfo = await _mediaService.GetCurrentMediaInfoAsync();
        Update(mediaInfo);
    }

    private async void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mediaInfo is null || _mediaService is null) return;
        if (_mediaInfo.IsPlaying) await _mediaService.PauseAsync();
        else await _mediaService.PlayAsync();
        var mediaInfo = await _mediaService.GetCurrentMediaInfoAsync();
        Update(mediaInfo);
    }

    private async void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mediaService is null) return;
        await _mediaService.SkipNextAsync();
        var mediaInfo = await _mediaService.GetCurrentMediaInfoAsync();
        Update(mediaInfo);
    }
}
