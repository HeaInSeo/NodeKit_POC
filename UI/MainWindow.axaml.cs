using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using NodeKit_POC.Models;
using NodeKit_POC.Services;

namespace NodeKit_POC.UI
{
    public partial class MainWindow : Window
    {
        private readonly ToolSourceExplorerService _toolSourceExplorerService = new();
        private ToolConnectivityMode _mode = ToolConnectivityMode.Disconnected;
        private ToolSourceStartPoint _startPoint = ToolSourceStartPoint.InternalSeed;
        private ToolSourceCandidate? _selectedCandidate;

        public MainWindow()
        {
            InitializeComponent();
            RegisterHandlers();
            ApplyModeUI();
            ApplyStartPointUI();
            ShowSelectionView();
        }

        private void RegisterHandlers()
        {
            DisconnectedModeButton.Click += (_, _) => SelectMode(ToolConnectivityMode.Disconnected);
            ConnectedModeButton.Click += (_, _) => SelectMode(ToolConnectivityMode.Connected);

            InternalSeedButton.Click += (_, _) => OpenExplorer(ToolSourceStartPoint.InternalSeed);
            LocalMirrorButton.Click += (_, _) => OpenExplorer(ToolSourceStartPoint.LocalPackageMirror);
            ExternalSearchButton.Click += (_, _) => OpenExplorer(ToolSourceStartPoint.ExternalSearch);
            RecipeBundleButton.Click += (_, _) => OpenExplorer(ToolSourceStartPoint.RecipeBundle);

            ExplorerInternalSeedButton.Click += (_, _) => SwitchExplorerStartPoint(ToolSourceStartPoint.InternalSeed);
            ExplorerLocalMirrorButton.Click += (_, _) => SwitchExplorerStartPoint(ToolSourceStartPoint.LocalPackageMirror);
            ExplorerExternalSearchButton.Click += (_, _) => SwitchExplorerStartPoint(ToolSourceStartPoint.ExternalSearch);
            ExplorerRecipeBundleButton.Click += (_, _) => SwitchExplorerStartPoint(ToolSourceStartPoint.RecipeBundle);

            BackToSelectionButton.Click += (_, _) => ShowSelectionView();
            SearchBox.TextChanged += (_, _) => RefreshCandidates();
            CandidatesListBox.SelectionChanged += OnCandidateSelectionChanged;
            StartWithCandidateButton.Click += OnStartWithCandidateClicked;
        }

        private void SelectMode(ToolConnectivityMode mode)
        {
            _mode = mode;
            if (_mode == ToolConnectivityMode.Disconnected && _startPoint == ToolSourceStartPoint.ExternalSearch)
            {
                _startPoint = ToolSourceStartPoint.InternalSeed;
            }

            ApplyModeUI();
            ApplyStartPointUI();

            if (ExplorerView.IsVisible)
            {
                RefreshCandidates();
            }
        }

        private void ShowSelectionView()
        {
            SelectionView.IsVisible = true;
            ExplorerView.IsVisible = false;
            Step2Badge.Background = new SolidColorBrush(Color.Parse("#16253F"));
            Step3Badge.Background = new SolidColorBrush(Color.Parse("#16253F"));
            UpdateStatus("Connected / Disconnected 모드와 시작점을 선택하세요.");
        }

        private void OpenExplorer(ToolSourceStartPoint startPoint)
        {
            if (startPoint == ToolSourceStartPoint.ExternalSearch && _mode == ToolConnectivityMode.Disconnected)
            {
                UpdateStatus("Disconnected 모드에서는 외부 검색을 사용할 수 없습니다.");
                return;
            }

            _startPoint = startPoint;
            SelectionView.IsVisible = false;
            ExplorerView.IsVisible = true;
            Step2Badge.Background = new SolidColorBrush(Color.Parse("#2F67FF"));
            Step3Badge.Background = new SolidColorBrush(Color.Parse("#2F67FF"));
            ApplyStartPointUI();
            RefreshCandidates();
            UpdateStatus($"{GetStartPointLabel(startPoint)}에서 tool 후보를 탐색하는 중입니다.");
        }

        private void SwitchExplorerStartPoint(ToolSourceStartPoint startPoint)
        {
            if (startPoint == ToolSourceStartPoint.ExternalSearch && _mode == ToolConnectivityMode.Disconnected)
            {
                UpdateStatus("Disconnected 모드에서는 외부 검색 탭이 비활성화됩니다.");
                return;
            }

            _startPoint = startPoint;
            ApplyStartPointUI();
            RefreshCandidates();
        }

        private void RefreshCandidates()
        {
            ExplorerModeChipText.Text = _mode == ToolConnectivityMode.Connected
                ? "Mode: Connected"
                : "Mode: Disconnected";
            ExplorerRouteChipText.Text = $"Start: {GetStartPointLabel(_startPoint)}";
            ExplorerStableGoalText.Text = $"Search: {(string.IsNullOrWhiteSpace(SearchBox.Text) ? "all fixtures" : SearchBox.Text!.Trim())}";
            CandidatesHeaderText.Text = $"{GetStartPointLabel(_startPoint)} 후보";
            ExplorerHintText.Text = GetExplorerHint();

            var query = SearchBox.Text ?? string.Empty;
            var candidates = _toolSourceExplorerService.SearchCandidates(_mode, _startPoint, query);

            CandidatesListBox.ItemsSource = candidates;
            EmptyCandidatesPanel.IsVisible = candidates.Count == 0;
            EmptyCandidatesText.Text = _mode == ToolConnectivityMode.Disconnected && _startPoint == ToolSourceStartPoint.ExternalSearch
                ? "외부 연결이 꺼져 있으므로 외부 검색 후보를 보여주지 않습니다."
                : $"검색어 '{query}'에 맞는 후보가 없습니다.";

            if (candidates.Count == 0)
            {
                CandidatesListBox.SelectedItem = null;
                UpdatePreview(null);
                return;
            }

            var preferred = _selectedCandidate != null
                ? candidates.FirstOrDefault(candidate =>
                    candidate.Route == _selectedCandidate.Route
                    && string.Equals(candidate.Name, _selectedCandidate.Name, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(candidate.Version, _selectedCandidate.Version, StringComparison.OrdinalIgnoreCase))
                : null;

            CandidatesListBox.SelectedItem = preferred ?? candidates[0];
        }

        private void OnCandidateSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            _selectedCandidate = CandidatesListBox.SelectedItem as ToolSourceCandidate;
            UpdatePreview(_selectedCandidate);
        }

        private void UpdatePreview(ToolSourceCandidate? candidate)
        {
            if (candidate == null)
            {
                PreviewTitleText.Text = "후보를 선택하세요.";
                PreviewSubtitleText.Text = "어떤 tool이든 선택한 source route가 ToolInstallRecipe 정규화의 입력이 됩니다.";
                PreviewRouteText.Text = "-";
                PreviewVersionText.Text = "-";
                PreviewStableRefText.Text = "-";
                PreviewConnectivityText.Text = "-";
                PreviewPackageText.Text = "-";
                PreviewMetadataText.Text = "-";
                GeneratedArtifactsText.Text = "ToolInstallRecipe draft, Dockerfile preview, environment.yml preview";
                PreviewHintText.Text = "Sprint 2에서는 후보 선택까지, Sprint 3부터 정규화와 Dockerfile 생성으로 이어집니다.";
                StartWithCandidateButton.IsEnabled = false;
                return;
            }

            PreviewTitleText.Text = candidate.DisplayName;
            PreviewSubtitleText.Text = $"{candidate.SourceLabel}에서 시작해 reproducible recipe 초안으로 이어집니다.";
            PreviewRouteText.Text = candidate.RouteLabel;
            PreviewVersionText.Text = candidate.Version;
            PreviewStableRefText.Text = candidate.StableRef;
            PreviewConnectivityText.Text = candidate.ConnectivityLabel;
            PreviewPackageText.Text = BuildPackageText(candidate);
            PreviewMetadataText.Text = candidate.MetadataSummary;
            GeneratedArtifactsText.Text = candidate.RequiresExternalConnection
                ? "ToolInstallRecipe draft, Dockerfile preview, environment.yml preview, external evidence summary"
                : "ToolInstallRecipe draft, Dockerfile preview, environment.yml preview";
            PreviewHintText.Text = candidate.RequiresExternalConnection
                ? "외부 정보는 초안 생성에만 사용하고, 이후 내부 digest와 검증 증거로 고정됩니다."
                : "현재 출발점은 외부 호출 없이도 재현 가능한 recipe 초안을 만들 수 있습니다.";
            StartWithCandidateButton.IsEnabled = true;
        }

        private void OnStartWithCandidateClicked(object? sender, RoutedEventArgs e)
        {
            if (_selectedCandidate == null)
            {
                return;
            }

            UpdateStatus($"선택 완료: {_selectedCandidate.DisplayName} → Sprint 3의 ToolInstallRecipe 정규화 단계로 이어질 준비가 되었습니다.");
        }

        private void ApplyModeUI()
        {
            SetButtonState(DisconnectedModeButton, _mode == ToolConnectivityMode.Disconnected, true);
            SetButtonState(ConnectedModeButton, _mode == ToolConnectivityMode.Connected, true);

            var externalEnabled = _mode == ToolConnectivityMode.Connected;
            SetButtonState(ExternalSearchButton, _startPoint == ToolSourceStartPoint.ExternalSearch, externalEnabled);
            SetButtonState(ExplorerExternalSearchButton, _startPoint == ToolSourceStartPoint.ExternalSearch, externalEnabled);

            ModeHintText.Text = _mode == ToolConnectivityMode.Connected
                ? "외부 소스 검색은 초안을 빨리 만드는 편의 기능입니다. 최종 재현성은 내부 고정 metadata와 digest로 확보합니다."
                : "이 모드에서는 외부 검색 없이도 내부 seed, 내부 미러, imported recipe만으로 시작할 수 있습니다.";

            SelectionHintText.Text = _mode == ToolConnectivityMode.Connected
                ? "Connected 모드에서는 4개 시작점이 모두 활성화됩니다."
                : "Disconnected 모드에서는 외부 검색 카드가 비활성화되고, 내부 시작점만 사용할 수 있습니다.";
        }

        private void ApplyStartPointUI()
        {
            SetButtonState(InternalSeedButton, _startPoint == ToolSourceStartPoint.InternalSeed, true);
            SetButtonState(LocalMirrorButton, _startPoint == ToolSourceStartPoint.LocalPackageMirror, true);
            SetButtonState(RecipeBundleButton, _startPoint == ToolSourceStartPoint.RecipeBundle, true);
            SetButtonState(ExternalSearchButton, _startPoint == ToolSourceStartPoint.ExternalSearch, _mode == ToolConnectivityMode.Connected);

            SetButtonState(ExplorerInternalSeedButton, _startPoint == ToolSourceStartPoint.InternalSeed, true);
            SetButtonState(ExplorerLocalMirrorButton, _startPoint == ToolSourceStartPoint.LocalPackageMirror, true);
            SetButtonState(ExplorerRecipeBundleButton, _startPoint == ToolSourceStartPoint.RecipeBundle, true);
            SetButtonState(ExplorerExternalSearchButton, _startPoint == ToolSourceStartPoint.ExternalSearch, _mode == ToolConnectivityMode.Connected);
        }

        private string GetExplorerHint()
        {
            return _startPoint switch
            {
                ToolSourceStartPoint.InternalSeed => "가장 적은 제스처로 바로 시작할 수 있는 내부 seed 후보를 보여줍니다. 현재 fixture: bwa, samtools, gatk, fastqc.",
                ToolSourceStartPoint.LocalPackageMirror => "내부 package mirror 또는 cache에서 재현 가능한 후보를 보여줍니다. 현재 fixture: bwa, samtools, fastqc.",
                ToolSourceStartPoint.ExternalSearch => "Connected 모드에서만 외부 후보를 탐색합니다. 현재 fixture: bwa, samtools, gatk, fastqc.",
                ToolSourceStartPoint.RecipeBundle => "미리 반입된 recipe bundle에서 시작할 수 있는 후보를 보여줍니다. 현재 fixture: bwa, samtools.",
                _ => string.Empty,
            };
        }

        private static string BuildPackageText(ToolSourceCandidate candidate)
        {
            if (!string.IsNullOrEmpty(candidate.PackageName) && !string.IsNullOrEmpty(candidate.Channel))
            {
                return $"{candidate.PackageName} / {candidate.Channel}";
            }

            if (!string.IsNullOrEmpty(candidate.PackageName))
            {
                return candidate.PackageName;
            }

            return candidate.SourceLabel;
        }

        private static string GetStartPointLabel(ToolSourceStartPoint startPoint)
        {
            return startPoint switch
            {
                ToolSourceStartPoint.InternalSeed => "내부 시드",
                ToolSourceStartPoint.LocalPackageMirror => "내부 미러",
                ToolSourceStartPoint.ExternalSearch => "외부 검색",
                ToolSourceStartPoint.RecipeBundle => "Recipe Bundle",
                _ => startPoint.ToString(),
            };
        }

        private static void SetButtonState(Button button, bool isSelected, bool isEnabled)
        {
            button.IsEnabled = isEnabled;

            if (!isEnabled)
            {
                button.Background = new SolidColorBrush(Color.Parse("#121828"));
                button.BorderBrush = new SolidColorBrush(Color.Parse("#24304D"));
                button.Foreground = new SolidColorBrush(Color.Parse("#60708F"));
                return;
            }

            button.Background = isSelected
                ? new SolidColorBrush(Color.Parse("#2F67FF"))
                : new SolidColorBrush(Color.Parse("#161C2E"));
            button.BorderBrush = isSelected
                ? new SolidColorBrush(Color.Parse("#6A95FF"))
                : new SolidColorBrush(Color.Parse("#283250"));
            button.Foreground = Brushes.White;
        }

        private void UpdateStatus(string text)
        {
            StatusText.Text = text;
        }
    }
}
