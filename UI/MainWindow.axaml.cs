using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using NodeKit_POC.Models;
using NodeKit_POC.Services;

namespace NodeKit_POC.UI
{
    public partial class MainWindow : Window
    {
        private readonly BiocondaSearchService _biocondaSearchService = new();
        private readonly ToolSourceExplorerService _toolSourceExplorerService = new();
        private ToolConnectivityMode _mode = ToolConnectivityMode.Connected;
        private ToolSourceStartPoint _startPoint = ToolSourceStartPoint.ExternalSearch;
        private ToolSourceCandidate? _selectedCandidate;
        private ToolSourceRoute? _externalRouteFilter = ToolSourceRoute.ExternalConda;

        public MainWindow()
        {
            InitializeComponent();
            RegisterHandlers();
            ApplyModeUI();
            ApplyStartPointUI();
            ShowSelectionView();
            UpdatePreview(null);
        }

        private void RegisterHandlers()
        {
            ConnectedModeButton.Click += (_, _) => SelectMode(ToolConnectivityMode.Connected);
            InternalSeedButton.Click += (_, _) => OpenExternalExplorer(ToolSourceRoute.ExternalConda);
            LocalMirrorButton.Click += (_, _) => OpenExternalExplorer(ToolSourceRoute.ExternalGitHubRelease);
            ExternalSearchButton.Click += (_, _) => OpenExternalExplorer(ToolSourceRoute.ExternalOciRegistry);

            ExplorerInternalSeedButton.Click += (_, _) => SetExternalRouteFilter(ToolSourceRoute.ExternalConda);
            ExplorerLocalMirrorButton.Click += (_, _) => SetExternalRouteFilter(ToolSourceRoute.ExternalGitHubRelease);
            ExplorerExternalSearchButton.Click += (_, _) => SetExternalRouteFilter(ToolSourceRoute.ExternalOciRegistry);
            ExplorerRecipeBundleButton.Click += (_, _) => SetExternalRouteFilter(null);

            BackToSelectionButton.Click += (_, _) => ShowSelectionView();
            SearchBox.TextChanged += (_, _) => _ = RefreshCandidatesAsync();
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
                _ = RefreshCandidatesAsync();
            }
        }

        private void ShowSelectionView()
        {
            SelectionView.IsVisible = true;
            ExplorerView.IsVisible = false;
            Step2Badge.Background = new SolidColorBrush(Color.Parse("#16253F"));
            Step3Badge.Background = new SolidColorBrush(Color.Parse("#16253F"));
            UpdateStatus("외부 연결 기반으로 시작합니다. 외부 검색 경로부터 검증하세요.");
        }

        private void OpenExplorer(ToolSourceStartPoint startPoint)
        {
            _startPoint = startPoint;
            SelectionView.IsVisible = false;
            ExplorerView.IsVisible = true;
            Step2Badge.Background = new SolidColorBrush(Color.Parse("#2F67FF"));
            Step3Badge.Background = new SolidColorBrush(Color.Parse("#2F67FF"));
            ApplyStartPointUI();
            _ = RefreshCandidatesAsync();
            UpdateStatus($"{GetStartPointLabel(startPoint)}에서 tool 후보를 탐색하는 중입니다.");
        }

        private void OpenExternalExplorer(ToolSourceRoute route)
        {
            _externalRouteFilter = route;
            OpenExplorer(ToolSourceStartPoint.ExternalSearch);
        }

        private void SetExternalRouteFilter(ToolSourceRoute? route)
        {
            _externalRouteFilter = route;
            ApplyStartPointUI();
            _ = RefreshCandidatesAsync();
        }

        private void SwitchExplorerStartPoint(ToolSourceStartPoint startPoint)
        {
            _startPoint = startPoint;
            ApplyStartPointUI();
            _ = RefreshCandidatesAsync();
        }

        private async Task RefreshCandidatesAsync()
        {
            ExplorerModeChipText.Text = _mode == ToolConnectivityMode.Connected
                ? "Mode: Connected"
                : "Mode: Disconnected";
            ExplorerRouteChipText.Text = $"Route: {GetExternalRouteFilterLabel()}";
            ExplorerStableGoalText.Text = $"Search: {(string.IsNullOrWhiteSpace(SearchBox.Text) ? "all fixtures" : SearchBox.Text!.Trim())}";
            CandidatesHeaderText.Text = $"{GetExternalRouteFilterLabel()} 후보";
            ExplorerHintText.Text = GetExplorerHint();

            var query = SearchBox.Text ?? string.Empty;
            UpdateStatus("후보를 조회하는 중입니다...");

            try
            {
                var candidates = await LoadCandidatesAsync(query).ConfigureAwait(true);

                CandidatesListBox.ItemsSource = candidates;
                EmptyCandidatesPanel.IsVisible = candidates.Count == 0;
                EmptyCandidatesText.Text = $"검색어 '{query}'에 맞는 외부 후보가 없습니다.";

                if (candidates.Count == 0)
                {
                    CandidatesListBox.SelectedItem = null;
                    UpdatePreview(null);
                    UpdateStatus("후보가 없습니다.");
                    return;
                }

                var preferred = _selectedCandidate != null
                    ? candidates.FirstOrDefault(candidate =>
                        candidate.Route == _selectedCandidate.Route
                        && string.Equals(candidate.Name, _selectedCandidate.Name, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(candidate.Version, _selectedCandidate.Version, StringComparison.OrdinalIgnoreCase))
                    : null;

                CandidatesListBox.SelectedItem = preferred ?? candidates[0];
                UpdateStatus($"{candidates.Count}개 후보를 표시했습니다.");
            }
            catch (Exception ex)
            {
                CandidatesListBox.ItemsSource = null;
                EmptyCandidatesPanel.IsVisible = true;
                EmptyCandidatesText.Text = $"외부 조회 실패: {ex.Message}";
                UpdatePreview(null);
                UpdateStatus($"외부 조회 실패: {ex.Message}");
            }
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
                EnvironmentYamlPreviewBox.Text = "# Conda package를 선택하면 environment.yml 초안이 생성됩니다.";
                DockerfilePreviewBox.Text = "# Conda package를 선택하면 multi-stage Dockerfile 초안이 생성됩니다.";
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
            GeneratedArtifactsText.Text = candidate.Route == ToolSourceRoute.ExternalConda
                ? "실제 Bioconda 검색 결과, environment.yml 초안, multi-stage Dockerfile 초안"
                : "후보 preview, route metadata, 후속 Dockerfile 생성 예정";
            PreviewHintText.Text = candidate.Route == ToolSourceRoute.ExternalConda
                ? "선택한 Conda package/version에서 바로 environment.yml과 multi-stage Dockerfile 초안을 생성합니다."
                : "이 경로의 실제 generator는 다음 단계에서 붙입니다. 현재는 route와 metadata preview를 우선 검증합니다.";
            ApplyRecipeDraftPreview(candidate);
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
            SetButtonState(DisconnectedModeButton, false, false);
            SetButtonState(ConnectedModeButton, _mode == ToolConnectivityMode.Connected, true);

            SetButtonState(InternalSeedButton, _externalRouteFilter == ToolSourceRoute.ExternalConda, true);
            SetButtonState(LocalMirrorButton, _externalRouteFilter == ToolSourceRoute.ExternalGitHubRelease, true);
            SetButtonState(ExternalSearchButton, _externalRouteFilter == ToolSourceRoute.ExternalOciRegistry, true);
            SetButtonState(ExplorerInternalSeedButton, _externalRouteFilter == ToolSourceRoute.ExternalConda, true);
            SetButtonState(ExplorerLocalMirrorButton, _externalRouteFilter == ToolSourceRoute.ExternalGitHubRelease, true);
            SetButtonState(ExplorerExternalSearchButton, _externalRouteFilter == ToolSourceRoute.ExternalOciRegistry, true);
            SetButtonState(ExplorerRecipeBundleButton, _externalRouteFilter == null, true);

            ModeHintText.Text = "현재 PoC는 외부 검색 기반 authoring 흐름을 먼저 검증합니다. 내부 seed, mirror, bundle은 이후 단계에서 붙입니다.";

            SelectionHintText.Text = "현재는 외부 시작 방법만 활성화되어 있습니다. Conda / GitHub Release / OCI Registry 중 어떤 경로로 recipe 초안을 시작할지 바로 고를 수 있습니다.";
        }

        private void ApplyStartPointUI()
        {
            SetButtonState(RecipeBundleButton, false, false);
            ApplyModeUI();
        }

        private string GetExplorerHint()
        {
            return _startPoint switch
            {
                ToolSourceStartPoint.ExternalSearch => _externalRouteFilter switch
                {
                    ToolSourceRoute.ExternalConda => "Bioconda / Conda 후보를 탐색합니다. package 이름과 버전에서 바로 recipe 초안으로 이어지기 가장 좋은 경로입니다.",
                    ToolSourceRoute.ExternalGitHubRelease => "GitHub Release 후보를 탐색합니다. release tag와 repository metadata를 기반으로 시작합니다.",
                    ToolSourceRoute.ExternalOciRegistry => "OCI Registry 후보를 탐색합니다. 기존 image를 runtime 출발점으로 활용하는 경로입니다.",
                    null => "문서에 적은 외부 경로 전체를 한 번에 비교합니다. Bioconda, GitHub Release, OCI Registry 후보를 함께 봅니다.",
                    _ => string.Empty,
                },
                ToolSourceStartPoint.InternalSeed => string.Empty,
                ToolSourceStartPoint.LocalPackageMirror => string.Empty,
                ToolSourceStartPoint.RecipeBundle => string.Empty,
                _ => string.Empty,
            };
        }

        private string GetExternalRouteFilterLabel()
        {
            return _externalRouteFilter switch
            {
                ToolSourceRoute.ExternalConda => "Bioconda",
                ToolSourceRoute.ExternalGitHubRelease => "GitHub Release",
                ToolSourceRoute.ExternalOciRegistry => "OCI Registry",
                null => "전체 외부",
                _ => "외부",
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

        private async Task<IReadOnlyList<ToolSourceCandidate>> LoadCandidatesAsync(string query)
        {
            if (_externalRouteFilter == ToolSourceRoute.ExternalConda)
            {
                return await _biocondaSearchService.SearchAsync(query).ConfigureAwait(true);
            }

            var fixtureCandidates = _toolSourceExplorerService
                .SearchCandidates(_mode, _startPoint, query)
                .Where(candidate => _externalRouteFilter == null || candidate.Route == _externalRouteFilter)
                .ToList();

            if (_externalRouteFilter == null)
            {
                var condaCandidates = await _biocondaSearchService.SearchAsync(query).ConfigureAwait(true);
                return condaCandidates
                    .Concat(fixtureCandidates.Where(candidate => candidate.Route != ToolSourceRoute.ExternalConda))
                    .ToList();
            }

            return fixtureCandidates;
        }

        private void ApplyRecipeDraftPreview(ToolSourceCandidate candidate)
        {
            if (candidate.Route != ToolSourceRoute.ExternalConda)
            {
                EnvironmentYamlPreviewBox.Text = "# 현재 route는 Conda generator가 아직 연결되지 않았습니다.";
                DockerfilePreviewBox.Text = "# 현재 route는 Conda generator가 아직 연결되지 않았습니다.";
                return;
            }

            var draft = CondaRecipeDraftGenerator.Generate(candidate);
            EnvironmentYamlPreviewBox.Text = draft.EnvironmentYaml;
            DockerfilePreviewBox.Text = draft.DockerfileContent;
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
