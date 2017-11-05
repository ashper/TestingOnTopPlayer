using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace TestOnTop
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private List<Subscription> Subscriptions;
        private List<Video> LatestSubscriptionsVideos;
        private UserCredential Credental;
        private bool HiddenMode;
        const string timespanFormat = "h':'m':'s";

        private bool isDraggingSlider = false;

        public event PropertyChangedEventHandler PropertyChanged;

        private double m_listBoxTemplateWidth;

        public double ListBoxTemplateWidth
        {
            get
            {
                return m_listBoxTemplateWidth;
            }
            private set
            {
                m_listBoxTemplateWidth = value;
                OnPropertyChanged("ListBoxTemplateWidth");
            }
        }

        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public MainWindow()
        {
            InitializeComponent();

            CommandBindings.Add(new CommandBinding(ApplicationCommands.Close,
               new ExecutedRoutedEventHandler(delegate (object sender, ExecutedRoutedEventArgs args) { this.Close(); })));

            Subscriptions = new List<Subscription>();
            LatestSubscriptionsVideos = new List<Video>();
            YoutubeLogin();
            LatestSubscriptionsVideos = LatestSubscriptionsVideos.OrderByDescending(x => x.Snippet.PublishedAt).ToList();

            var test2 = LatestSubscriptionsVideos.Select(x => new YoutubeVideoVitals { Title = x.Snippet.Title, ImageUrl = x.Snippet.Thumbnails.Standard?.Url, Id = x.Id });

            lbVideos.ItemsSource = test2;
            ListBoxTemplateWidth = 100;
            HiddenMode = false;
        }

        public void DragWindow(object sender, MouseButtonEventArgs args)
        {
            DragMove();
        }

        private void YoutubeLogin()
        {
            using (var stream = new FileStream("client_secret_90216717933-jqbg5psb20erpgsfatfrd7sjce2uo3s1.apps.googleusercontent.com.json", FileMode.Open, FileAccess.Read))
            {
                Credental = GoogleWebAuthorizationBroker.AuthorizeAsync(
                    GoogleClientSecrets.Load(stream).Secrets,
                    new[] { YouTubeService.Scope.YoutubeReadonly },
                    "user",
                    CancellationToken.None,
                    new FileDataStore(this.GetType().ToString())
                ).Result;
            }

            var youtubeService = new YouTubeService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = Credental,
                ApplicationName = this.GetType().ToString()
            });

            var test = new SubscriptionsResource.ListRequest(youtubeService, "snippet")
            {
                Mine = true,
                MaxResults = 50
            };
            var test2 = test.Execute();

            Subscriptions.AddRange(test2.Items);

            GetLatestSubscriptionVideos(Subscriptions);
        }

        private void GetLatestSubscriptionVideos(List<Subscription> subscriptions)
        {
            ConcurrentBag<Video> videos = new ConcurrentBag<Video>();
            var options = new ParallelOptions();
            options.MaxDegreeOfParallelism = 10;
            Parallel.ForEach(subscriptions, options, sub =>
            {
                foreach (var v in GetSubscriptionLatestVideosAsync(sub))
                {
                    videos.Add(v);
                }
            });
            LatestSubscriptionsVideos.AddRange(videos);
        }

        private IEnumerable<Video> GetSubscriptionLatestVideosAsync(Subscription subscription)
        {
            var latestVideosToGrab = 5;

            var youtubeService = new YouTubeService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = Credental,
                ApplicationName = this.GetType().ToString()
            });

            var test1 = new ChannelsResource.ListRequest(youtubeService, "contentDetails");
            test1.MaxResults = 1;
            test1.Id = subscription.Snippet.ResourceId.ChannelId;
            var test2 = test1.Execute();
            var test3 = test2.Items;
            if (!(test3.Count <= 0))
            {
                var uploadPlaylistId = test3[0].ContentDetails.RelatedPlaylists.Uploads;

                var playlistRequest = youtubeService.PlaylistItems.List("snippet");
                playlistRequest.MaxResults = latestVideosToGrab;
                playlistRequest.PlaylistId = uploadPlaylistId;
                var test4 = playlistRequest.Execute();

                var idList = new List<string>();
                foreach (var pl in test4.Items)
                {
                    idList.Add(pl.Snippet.ResourceId.VideoId);
                }

                var videosRequest = youtubeService.Videos.List("snippet,contentDetails,statistics");
                videosRequest.MaxResults = latestVideosToGrab;
                videosRequest.Id = string.Join(",", idList);
                var test5 = videosRequest.Execute();

                var videoList = test5.Items;

                return videoList;
            }
            else
            {
                return new List<Video>();
            }
        }

        private Double CalcThumbWidth()
        {
            return this.ActualWidth / 5.1;
        }

        private void StartVideo(string Url)
        {
            vlcPlayer.LoadMedia(Url);

            vlcPlayer.Play();
            vlcPlayer.TimeChanged += VlcPlayer_TimeChanged;
        }

        private void VlcPlayer_TimeChanged(object sender, EventArgs e)
        {
            if (isDraggingSlider == false)
            {
                this.Dispatcher.Invoke(() =>
                {
                    vlcPlayer.VerticalContentAlignment = VerticalAlignment.Top;
                    Point relativePoint = VlcSlider.TransformToAncestor(PanelVLC)
                          .Transform(new Point(0, 0));
                    vlcPlayer.Height = relativePoint.Y;
                    VlcMediaTotalTime.Text = vlcPlayer.Length.ToString(timespanFormat);
                    VlcSlider.Maximum = vlcPlayer.Length.TotalMilliseconds;
                    VlcSlider.Value = vlcPlayer.Time.TotalMilliseconds;
                    VlcSliderTime.Text = TimeSpan.FromMilliseconds(VlcSlider.Value).ToString(timespanFormat);
                });
            }
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ListBoxTemplateWidth = CalcThumbWidth();
        }

        private void lbVideos_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var item = ItemsControl.ContainerFromElement(sender as ListBox, e.OriginalSource as DependencyObject) as ListBoxItem;
            if (item != null)
            {
                PanelVLC.Background = Brushes.White;
                lbVideos.Visibility = Visibility.Hidden;
                PanelVLC.Visibility = Visibility.Visible;

                var vitals = (YoutubeVideoVitals)item.DataContext;

                var youtube = VideoLibrary.YouTube.Default;
                var videosDetails = youtube.GetAllVideos($"https://www.youtube.com/watch?v={vitals.Id}");

                var test = videosDetails.Where(x => x.Format == VideoLibrary.VideoFormat.Mp4 && x.AudioFormat != VideoLibrary.AudioFormat.Unknown);
                var test2 = test.First().Uri;
                StartVideo(test2);
                HiddenMode = false;
            }
        }

        private void myWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.G)
            {
                vlcPlayer.Stop();
                lbVideos.Visibility = Visibility.Visible;
                PanelVLC.Background = Brushes.White;
                PanelVLC.Visibility = Visibility.Hidden;
                TopBar.Visibility = Visibility.Visible;
                this.ResizeMode = ResizeMode.CanResizeWithGrip;

                var hwnd = new WindowInteropHelper(this).Handle;
                WindowsServices.SetWindowNormal(hwnd);
                HiddenMode = false;
            }
            else if (e.Key == Key.H)
            {
                if (PanelVLC.Visibility == Visibility.Visible)
                {
                    if (HiddenMode == true)
                    {
                        this.ResizeMode = ResizeMode.CanResizeWithGrip;
                        TopBar.Visibility = Visibility.Visible;
                        lbVideos.Visibility = Visibility.Visible;
                        VlcSlider.Visibility = Visibility.Visible;
                        PanelVLC.Background = Brushes.White;

                        Thickness margin = PanelVLC.Margin;
                        margin.Top = 0;
                        PanelVLC.Margin = margin;

                        var hwnd = new WindowInteropHelper(this).Handle;
                        WindowsServices.SetWindowNormal(hwnd);
                        HiddenMode = false;
                    }
                    else
                    {
                        this.ResizeMode = ResizeMode.NoResize;
                        TopBar.Visibility = Visibility.Hidden;
                        lbVideos.Visibility = Visibility.Hidden;
                        VlcSlider.Visibility = Visibility.Hidden;
                        PanelVLC.Background = Brushes.Transparent;

                        Thickness margin = PanelVLC.Margin;
                        margin.Top = -30;
                        PanelVLC.Margin = margin;

                        var hwnd = new WindowInteropHelper(this).Handle;
                        WindowsServices.SetWindowExTransparent(hwnd);
                        HiddenMode = true;
                    }
                }
            }
            else if (e.Key == Key.Space)
            {
                if (vlcPlayer.VlcMediaPlayer.CanPause || vlcPlayer.VlcMediaPlayer.CanPlay)
                {
                    vlcPlayer.VlcMediaPlayer.PauseOrResume();
                }
            }
        }

        private void VlcSlider_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            vlcPlayer.Time = TimeSpan.FromMilliseconds(VlcSlider.Value);
            isDraggingSlider = false;
        }

        private void VlcSlider_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
        {
            isDraggingSlider = true;
        }

        private void VlcSlider_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        {
            if (isDraggingSlider)
            {
                VlcSliderTime.Text = TimeSpan.FromMilliseconds(VlcSlider.Value).ToString(timespanFormat);
            }
        }
    }

    public static class WindowsServices
    {
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LEFT = 0x00000000;
        private const int GWL_EXSTYLE = (-20);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        public static void SetWindowExTransparent(IntPtr hwnd)
        {
            var extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT);
        }

        public static void SetWindowNormal(IntPtr hwnd)
        {
            var extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, WS_EX_LEFT);
        }
    }
}