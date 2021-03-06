﻿/***

   Copyright (C) 2018-2019. dc-koromo. All Rights Reserved.
   
   Author: Koromo Copy Developer

***/

using Koromo_Copy;
using Koromo_Copy.Component.Hitomi;
using Koromo_Copy.Component.Hitomi.Analysis;
using Koromo_Copy_UX.Domain;
using Koromo_Copy_UX.Utility.Bookmark;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Koromo_Copy_UX
{
    /// <summary>
    /// ArtistViewerWindow.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class ArtistViewerWindow : Window
    {
        public ArtistViewerWindow()
        {
            InitializeComponent();
            Koromo_Copy_UX.Language.Lang.ApplyLanguageDictionary(this);

            DataContext = new Domain.ArtistDataGridViewModel();

            Task.Run(() =>
            {
                var result = HitomiDataParser.SearchAsync("artist:michiking").Result;
                _ = Task.Run(() => LoadThumbnail(result));
            });
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (e.Key == Key.Escape)
                Close();
        }

        HitomiPortableAnalysis hpa = new HitomiPortableAnalysis();
        int current_load = 0;
        int current_item = 0;
        bool loaded = false;

        public ArtistViewerWindow(string artist)
        {
            InitializeComponent();
            Koromo_Copy_UX.Language.Lang.ApplyLanguageDictionary(this);

            DataContext = new Domain.ArtistDataGridViewModel();
            TagList.Sorting += new DataGridSortingEventHandler(new DataGridSorter<ArtistDataGridItemViewModel>(TagList).SortHandler);
            Title = $"{FindResource("artist")} : {artist}";
            Artist = artist;

            if (Settings.Instance.Hitomi.DisableArtistViewToast)
                ArtistsPopup.IsOpen = false;

            var dictionary = new Dictionary<string, int>();
            Task.Run(() =>
            {
                var result = HitomiDataParser.SearchAsync($"artist:{artist.ToLower().Replace(' ', '_')}").Result;
                _ = Task.Run(() => LoadThumbnail(result));

                foreach (var md in result)
                {
                    if (md.Tags != null)
                        foreach (var _tag in md.Tags)
                        {
                            var tag = HitomiIndex.Instance.index.Tags[_tag];
                            if (dictionary.ContainsKey(tag))
                                dictionary[tag] += 1;
                            else
                                dictionary.Add(tag, 1);
                        }
                }
            }).ContinueWith(async t => {
                var vm = DataContext as Domain.ArtistDataGridViewModel;
                var list = dictionary.ToList();
                list.Sort((a, b) => b.Value.CompareTo(a.Value));
                foreach (var tag in list)
                    vm.Items.Add(new Domain.ArtistDataGridItemViewModel
                    {
                        항목=tag.Key,
                        카운트=tag.Value
                    });
                hpa.CustomAnalysis = dictionary.Select(x => new Tuple<string, int>(x.Key, x.Value)).ToList();

                if (!Settings.Instance.Hitomi.DisableArtistViewToast)
                {
                    await Task.Run(() => hpa.Update());
                    for (int j = 0; j < 5 && current_item < hpa.Rank.Count; current_item++)
                    {
                        if (hpa.Rank[current_item].Item1 == Artist) continue;
                        RecommendArtist.Children.Add(new ArtistViewerToastElements(
                            $"{current_load + 1}. {hpa.Rank[current_item].Item1} ({HitomiAnalysis.Instance.ArtistCount[hpa.Rank[current_item].Item1]})", $"{FindResource("score")}: {hpa.Rank[current_item].Item2}", hpa.Rank[current_item].Item1));
                        j++;
                        current_load++;
                    }
                    loaded = true;
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
        
        public string Artist;

        private void LoadThumbnail(List<HitomiIndexMetadata> md)
        {
            List<Task> task = new List<Task>();
            foreach (var metadata in md)
            {
                Task.Run(() => LoadThumbnail(metadata));
                Thread.Sleep(100);
            }
        }

        private void LoadThumbnail(HitomiIndexMetadata md)
        {
            Application.Current.Dispatcher.Invoke(new Action(
            delegate
            {
                // Put code that needs to run on the UI thread here
                var se = new SearchSimpleElements(HitomiLegalize.MetadataToArticle(md));
                ArticlePanel.Children.Add(se);
                Koromo_Copy.Monitor.Instance.Push("[AddSearchElements] Hitomi Metadata " + md.ID);
            }));
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // We know longer need to size to the contents.
            ClearValue(SizeToContentProperty);
            // We want our control to shrink/expand with the window.
            ArticlePanel.ClearValue(WidthProperty);
            ArticlePanel.ClearValue(HeightProperty);
            TagList.ClearValue(WidthProperty);
            TagList.ClearValue(HeightProperty);
            // Don't want our window to be able to get any smaller than this.
            SetValue(MinWidthProperty, this.Width);
            SetValue(MinHeightProperty, this.Height);

            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);

            ArtistsPopup.MaxHeight = 689;
        }

        private void DataGridRow_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var tags = TagList.SelectedItems.OfType<ArtistDataGridItemViewModel>().Select(x => x.항목);
            foreach (var control in ArticlePanel.Children.OfType<SearchSimpleElements>())
            {
                if (tags.All(x => (control.Article as HitomiArticle).Tags != null && (control.Article as HitomiArticle).Tags.Contains(x)))
                    control.Select = true;
                else
                {
                    control.Select = false;
                    control.Transparent();
                }
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;

            if (btn.Tag.ToString() == "DownloadAll")
            {
                try
                {
                    int count = 0;
                    ArticlePanel.Children.OfType<SearchSimpleElements>().ToList().ForEach(x =>
                    {
                        var ha = x.Article as HitomiArticle;
                        ha.Artists = new string[] { Artist };
                        var prefix = HitomiCommon.MakeDownloadDirectory(ha);
                        Directory.CreateDirectory(prefix);
                        if (!ha.IsUnstable)
                        {
                            DownloadSpace.Instance.RequestDownload(x.Article.Title,
                                x.Article.ImagesLink.Select(y => HitomiCommon.GetDownloadImageAddress((x.Article as HitomiArticle).Magic, y)).ToArray(),
                                x.Article.ImagesLink.Select(y => Path.Combine(prefix, y)).ToArray(),
                                Koromo_Copy.Interface.SemaphoreExtends.Default, prefix, x.Article);
                        }
                        else
                        {
                            DownloaderHelper.ProcessUnstable(ha.UnstableModel);
                        }
                        count++;
                    });
                    if (count > 0) MainWindow.Instance.FadeOut_MiddlePopup($"{count}{FindResource("msg_download_start")}");
                    MainWindow.Instance.Activate();
                    MainWindow.Instance.FocusDownload();
                    Close();
                }
                catch (Exception ex)
                {
                    Koromo_Copy.Monitor.Instance.Push("[Artist Viewer] " + ex.Message);
                    MessageBox.Show($"{FindResource("msg_wait_fucking_loading")}", "Koromo Copy", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else if (btn.Tag.ToString() == "Download")
            {
                try
                {
                    int count = 0;
                    ArticlePanel.Children.OfType<SearchSimpleElements>().ToList().Where(x => x.Select).ToList().ForEach(x =>
                    {
                        var ha = x.Article as HitomiArticle;
                        ha.Artists = new string[] { Artist };
                        var prefix = HitomiCommon.MakeDownloadDirectory(ha);
                        Directory.CreateDirectory(prefix);
                        if (!ha.IsUnstable)
                        {
                            DownloadSpace.Instance.RequestDownload(x.Article.Title,
                                x.Article.ImagesLink.Select(y => HitomiCommon.GetDownloadImageAddress((x.Article as HitomiArticle).Magic, y)).ToArray(),
                                x.Article.ImagesLink.Select(y => Path.Combine(prefix, y)).ToArray(),
                                Koromo_Copy.Interface.SemaphoreExtends.Default, prefix, x.Article);
                        }
                        else
                        {
                            DownloaderHelper.ProcessUnstable(ha.UnstableModel);
                        }
                        count++;
                    });
                    if (count > 0) MainWindow.Instance.FadeOut_MiddlePopup($"{count}{FindResource("msg_download_start")}");
                    MainWindow.Instance.Activate();
                    MainWindow.Instance.FocusDownload();
                }
                catch (Exception ex)
                {
                    Koromo_Copy.Monitor.Instance.Push("[Artist Viewer] " + ex.Message);
                    MessageBox.Show($"{FindResource("msg_wait_fucking_all_loading")}", "Koromo Copy", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

        }

        public static RoutedCommand Command = new RoutedCommand();
        private void CommandBinding_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            ProcessCommand((e.Parameter as string)[0]);
        }

        private void MenuItem_Click(object sender, RoutedEventArgs e)
        {
            ProcessCommand((sender as MenuItem).Tag.ToString()[0]);
        }

        private void ProcessCommand(char c)
        {
            switch (c)
            {
                case 'A':
                    ArticlePanel.Children.OfType<SearchSimpleElements>().ToList().ForEach(x => x.Select = true);
                    break;

                case 'C':
                    ArticlePanel.Children.OfType<SearchSimpleElements>().ToList().ForEach(x => {
                        x.Select = false;
                        x.Transparent();
                    });
                    break;

                case 'R':
                    ArticlePanel.Children.OfType<SearchSimpleElements>().ToList().ForEach(x => {
                        x.Select = !x.Select;
                        if (!x.Select) x.Transparent();
                    });
                    break;

                case 'S':
                    List<string> titles = new List<string>();
                    for (int i = 0; i < ArticlePanel.Children.Count; i++)
                    {
                        string ttitle = (ArticlePanel.Children[i] as SearchSimpleElements).Article.Title.Split('|')[0];
                        if (titles.Count > 0 && !titles.TrueForAll((title) => Strings.ComputeLevenshteinDistance(ttitle, title) > Settings.Instance.Hitomi.TextMatchingAccuracy))
                        {
                            (ArticlePanel.Children[i] as SearchSimpleElements).Select = false;
                            (ArticlePanel.Children[i] as SearchSimpleElements).Transparent();
                            continue;
                        }

                        titles.Add(ttitle);
                    }
                    break;

                case 'G':
                    ArticlePanel.Children.OfType<SearchSimpleElements>().ToList().ForEach(x => {
                        if (HitomiLog.Instance.Contains((x.Article as HitomiArticle).Magic))
                        {
                            x.Select = false;
                            x.Transparent();
                        }
                    });
                    break;

                case 'B':
                    BookmarkModelManager.Instance.Model.artists.Add(new Tuple<string, BookmarkItemModel>("/미분류", new BookmarkItemModel
                    {
                        stamp = DateTime.Now,
                        content = Artist.Replace('_', ' '),
                        path = ""
                    }));
                    BookmarkModelManager.Instance.Save();
                    MessageBox.Show("북마크에 추가되었습니다!", "Koromo Copy", MessageBoxButton.OK, MessageBoxImage.Information);
                    break;

                case 'D':
                    break;
            }
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var offset = ArtistsPopup.HorizontalOffset;
            ArtistsPopup.HorizontalOffset = offset + 1;
            ArtistsPopup.HorizontalOffset = offset;
        }

        private void Window_LocationChanged(object sender, EventArgs e)
        {
            var offset = ArtistsPopup.HorizontalOffset;
            ArtistsPopup.HorizontalOffset = offset + 1;
            ArtistsPopup.HorizontalOffset = offset;
        }

        private void Border_MouseEnter(object sender, MouseEventArgs e)
        {
            CoverRectangle.Background = new SolidColorBrush(Colors.Gray);
        }

        private void Border_MouseLeave(object sender, MouseEventArgs e)
        {
            CoverRectangle.Background = new SolidColorBrush(Colors.Transparent);
        }

        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!loaded) return;
            
            for (int j = 0; j < 5 && current_item < hpa.Rank.Count; current_item++)
            {
                if (hpa.Rank[current_item].Item1 == Artist) continue;
                RecommendArtist.Children.Add(new ArtistViewerToastElements(
                    $"{current_load + 1}. {hpa.Rank[current_item].Item1} ({HitomiAnalysis.Instance.ArtistCount[hpa.Rank[current_item].Item1]})", $"{FindResource("score")}: {hpa.Rank[current_item].Item2}", hpa.Rank[current_item].Item1));
                j++;
                current_load++;
            }
        }

        private void Border_MouseLeave_1(object sender, MouseEventArgs e)
        {
            CoverRectangleTop.Background = new SolidColorBrush(Colors.Transparent);
        }

        private void Border_MouseEnter_1(object sender, MouseEventArgs e)
        {
            CoverRectangleTop.Background = new SolidColorBrush(Colors.Gray);
        }

        private void Border_MouseLeftButtonDown_1(object sender, MouseButtonEventArgs e)
        {
            if (!loaded) return;
            if (e.ClickCount == 2)
            {
                HitomiAnalysis.Instance.UserDefined = true;
                HitomiAnalysis.Instance.CustomAnalysis = hpa.CustomAnalysis;
                RecommendSpace.Instance.Update();
                MainWindow.Instance.Activate();
                MainWindow.Instance.FocusRecommend();
            }
        }
    }
}
