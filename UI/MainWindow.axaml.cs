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
        private ToolSourceStartPoint _selectedStartPoint = ToolSourceStartPoint.ExternalSearch;
        private ToolSourceRoute? _selectedRouteFilter = ToolSourceRoute.ExternalConda;
        private ToolSourceCandidate? _selectedCandidate;
        private ToolInstallRecipe? _selectedRecipe;
        private GeneratedToolImageRecipe? _generatedRecipe;

        public MainWindow()
        {
            InitializeComponent();
            RegisterHandlers();
            ApplyModeUI();
            ApplyRouteFilterUI();
            ShowSelectionView();
            UpdateCandidatePreview(null);
        }

        private void RegisterHandlers()
        {
            ConnectedModeButton.Click += (_, _) => SelectMode(ToolConnectivityMode.Connected);
            DisconnectedModeButton.Click += (_, _) => SelectMode(ToolConnectivityMode.Disconnected);

            BiocondaStartButton.Click += (_, _) => OpenExplorer(ToolSourceStartPoint.ExternalSearch, ToolSourceRoute.ExternalConda);
            GitHubReleaseStartButton.Click += (_, _) => OpenExplorer(ToolSourceStartPoint.ExternalSearch, ToolSourceRoute.ExternalGitHubRelease);
            OciRegistryStartButton.Click += (_, _) => OpenExplorer(ToolSourceStartPoint.ExternalSearch, ToolSourceRoute.ExternalOciRegistry);
            AllExternalRoutesStartButton.Click += (_, _) => OpenExplorer(ToolSourceStartPoint.ExternalSearch, null);

            InternalSeedStartButton.Click += (_, _) => OpenExplorer(ToolSourceStartPoint.InternalSeed, ToolSourceRoute.InternalSeed);
            LocalMirrorStartButton.Click += (_, _) => OpenExplorer(ToolSourceStartPoint.LocalPackageMirror, ToolSourceRoute.LocalPackageMirror);
            RecipeBundleStartButton.Click += (_, _) => OpenExplorer(ToolSourceStartPoint.RecipeBundle, ToolSourceRoute.RecipeBundle);
            InternalRegistryStartButton.Click += (_, _) => OpenExplorer(ToolSourceStartPoint.InternalOciRegistry, ToolSourceRoute.InternalOciRegistry);

            BiocondaFilterButton.Click += (_, _) => SetRouteFilter(ToolSourceRoute.ExternalConda);
            GitHubReleaseFilterButton.Click += (_, _) => SetRouteFilter(ToolSourceRoute.ExternalGitHubRelease);
            OciRegistryFilterButton.Click += (_, _) => SetRouteFilter(ToolSourceRoute.ExternalOciRegistry);
            AllExternalFilterButton.Click += (_, _) => SetRouteFilter(null);
            InternalSeedFilterButton.Click += (_, _) => SetRouteFilter(ToolSourceRoute.InternalSeed);
            LocalMirrorFilterButton.Click += (_, _) => SetRouteFilter(ToolSourceRoute.LocalPackageMirror);
            RecipeBundleFilterButton.Click += (_, _) => SetRouteFilter(ToolSourceRoute.RecipeBundle);
            InternalRegistryFilterButton.Click += (_, _) => SetRouteFilter(ToolSourceRoute.InternalOciRegistry);

            BackToSelectionButton.Click += (_, _) => ShowSelectionView();
            BackToExplorerButton.Click += (_, _) => ShowExplorerView();
            SearchBox.TextChanged += (_, _) => _ = RefreshCandidatesAsync();
            CandidatesListBox.SelectionChanged += OnCandidateSelectionChanged;
            OpenRecipePreviewButton.Click += OnOpenRecipePreviewClicked;
        }

        private void SelectMode(ToolConnectivityMode mode)
        {
            _mode = mode;

            if (_mode == ToolConnectivityMode.Connected)
            {
                _selectedStartPoint = ToolSourceStartPoint.ExternalSearch;
                _selectedRouteFilter ??= ToolSourceRoute.ExternalConda;
            }
            else
            {
                _selectedStartPoint = ToolSourceStartPoint.InternalSeed;
                _selectedRouteFilter = ToolSourceRoute.InternalSeed;
            }

            ApplyModeUI();
            ApplyRouteFilterUI();

            if (ExplorerView.IsVisible)
            {
                _ = RefreshCandidatesAsync();
            }
        }

        private void ShowSelectionView()
        {
            SelectionView.IsVisible = true;
            ExplorerView.IsVisible = false;
            RecipePreviewView.IsVisible = false;
            ApplyStepState(1);
            UpdateStatus("Connected 또는 Disconnected 모드와 시작점을 선택하세요.");
        }

        private void ShowExplorerView()
        {
            SelectionView.IsVisible = false;
            ExplorerView.IsVisible = true;
            RecipePreviewView.IsVisible = false;
            ApplyStepState(2);
            UpdateStatus("후보를 선택하고 생성 예정 항목을 확인하세요.");
        }

        private void OpenExplorer(ToolSourceStartPoint startPoint, ToolSourceRoute? routeFilter)
        {
            _selectedStartPoint = startPoint;
            _selectedRouteFilter = routeFilter;
            _selectedCandidate = null;
            _selectedRecipe = null;
            _generatedRecipe = null;

            ShowExplorerView();
            ApplyModeUI();
            ApplyRouteFilterUI();
            _ = RefreshCandidatesAsync();
            UpdateStatus($"{GetStartPointLabel(startPoint)} 탐색을 시작합니다.");
        }

        private void SetRouteFilter(ToolSourceRoute? route)
        {
            _selectedRouteFilter = route;
            ApplyRouteFilterUI();
            _ = RefreshCandidatesAsync();
        }

        private void ShowRecipePreviewView()
        {
            SelectionView.IsVisible = false;
            ExplorerView.IsVisible = false;
            RecipePreviewView.IsVisible = true;
            ApplyStepState(3);
            UpdateStatus("ToolInstallRecipe와 generated image recipe 초안을 확인하세요.");
        }

        private async Task RefreshCandidatesAsync()
        {
            ExplorerModeChipText.Text = _mode == ToolConnectivityMode.Connected
                ? "Mode: Connected"
                : "Mode: Disconnected";
            ExplorerRouteChipText.Text = $"Route: {GetRouteFilterLabel()}";
            ExplorerSearchChipText.Text = $"Search: {(string.IsNullOrWhiteSpace(SearchBox.Text) ? "all" : SearchBox.Text!.Trim())}";
            CandidatesHeaderText.Text = $"{GetRouteFilterLabel()} 후보";
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
                    UpdateCandidatePreview(null);
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
                EmptyCandidatesText.Text = $"후보 조회 실패: {ex.Message}";
                UpdateCandidatePreview(null);
                UpdateStatus($"후보 조회 실패: {ex.Message}");
            }
        }

        private void OnCandidateSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            _selectedCandidate = CandidatesListBox.SelectedItem as ToolSourceCandidate;
            UpdateCandidatePreview(_selectedCandidate);
        }

        private void UpdateCandidatePreview(ToolSourceCandidate? candidate)
        {
            if (candidate == null)
            {
                PreviewTitleText.Text = "후보를 선택하세요.";
                PreviewSubtitleText.Text = "선택한 후보가 ToolInstallRecipe 정규화의 입력이 됩니다.";
                PreviewRouteText.Text = "-";
                PreviewVersionText.Text = "-";
                PreviewStableRefText.Text = "-";
                PreviewPackageText.Text = "-";
                PreviewConnectivityText.Text = "-";
                PreviewOutputsText.Text = "environment.yml, multi-stage Dockerfile, lock metadata preview";
                PreviewHintText.Text = "Source Explorer에서는 비교와 선택만 하고, Dockerfile 전문은 Recipe Preview에서 확인합니다.";
                OpenRecipePreviewButton.IsEnabled = false;
                return;
            }

            PreviewTitleText.Text = candidate.DisplayName;
            PreviewSubtitleText.Text = $"{candidate.SourceLabel}에서 시작해 reproducible runtime image recipe 초안으로 이어집니다.";
            PreviewRouteText.Text = candidate.RouteLabel;
            PreviewVersionText.Text = candidate.Version;
            PreviewStableRefText.Text = candidate.StableRef;
            PreviewPackageText.Text = BuildPackageText(candidate);
            PreviewConnectivityText.Text = candidate.ConnectivityLabel;
            PreviewOutputsText.Text = candidate.Route is ToolSourceRoute.ExternalConda or ToolSourceRoute.LocalPackageMirror or ToolSourceRoute.InternalSeed
                ? "environment.yml, multi-stage Dockerfile, lock metadata preview"
                : "ToolInstallRecipe preview, route metadata, runtime image recipe draft";
            PreviewHintText.Text = candidate.Route switch
            {
                ToolSourceRoute.ExternalConda => "Conda / Bioconda 경로는 실제 channel metadata를 사용해 package 후보를 찾습니다. lock metadata는 아직 preview-only이며, 실행 command는 이후 단계에서 정의합니다.",
                ToolSourceRoute.LocalPackageMirror => "내부 미러 경로는 local channel을 사용해 environment.yml을 생성합니다. 외부 bioconda 채널을 그대로 사용하지 않습니다.",
                ToolSourceRoute.InternalSeed => "내부 시드 레시피도 실제 설치 가능한 runtime recipe로 정규화됩니다. 이 이미지는 실행 노드가 아니라 tool runtime 재료입니다.",
                _ => "이 route는 아직 부분 fixture 기반이지만 Candidate → Recipe → Generated 구조로 정규화됩니다. 실제 command와 entrypoint는 이후 wrapper 단계에서 정의합니다.",
            };
            OpenRecipePreviewButton.IsEnabled = true;
        }

        private void OnOpenRecipePreviewClicked(object? sender, RoutedEventArgs e)
        {
            if (_selectedCandidate == null)
            {
                return;
            }

            _selectedRecipe = ToolInstallRecipeNormalizer.Normalize(_selectedCandidate);
            _generatedRecipe = ToolImageRecipeGenerator.Generate(_selectedRecipe);
            PopulateRecipePreview();
            ShowRecipePreviewView();
        }

        private void PopulateRecipePreview()
        {
            if (_selectedRecipe == null || _generatedRecipe == null)
            {
                return;
            }

            RecipeToolText.Text = _selectedRecipe.Name;
            RecipeVersionText.Text = _selectedRecipe.Version;
            RecipePrimaryToolText.Text = $"{_selectedRecipe.Name} ({_selectedRecipe.Version})";
            RecipeModeText.Text = _mode == ToolConnectivityMode.Connected ? "Connected" : "Disconnected";
            RecipeRouteText.Text = _selectedRecipe.SourceRoute.ToString();
            RecipeInstallMethodText.Text = _selectedRecipe.InstallMethod.ToString();
            RecipeStableRefText.Text = _selectedRecipe.StableRef;
            RecipeVersionTagText.Text = _selectedRecipe.RecipeVersion;
            RecipeFingerprintText.Text = _generatedRecipe.RecipeFingerprint;
            RecipeReproSeedText.Text = _selectedRecipe.ReproducibilitySeed;
            RuntimePolicyText.Text = "이 이미지는 단일 Tool runtime image 재료입니다. 특정 command 또는 entrypoint를 고정하지 않으며, 실제 실행 script와 command는 이후 wrapper 또는 DAG node 이미지 단계에서 정의합니다.";
            RecipePackagesBox.Text = _selectedRecipe.Packages.Count == 0
                ? "# primary tool package preview unavailable"
                : string.Join(
                    "\n",
                    _selectedRecipe.Packages.Select(package =>
                        $"{package.Name}={package.Version} channel={package.Channel} platform={package.Platform}"));
            ResolvedDependenciesBox.Text = "preview-only\nlock 단계에서 의존 패키지와 build string이 확정됩니다.";
            LockMetadataBox.Text = _generatedRecipe.LockMetadata ?? "# lock metadata preview unavailable";
            EnvironmentYamlPreviewBox.Text = _generatedRecipe.EnvironmentYaml ?? "# environment.yml preview unavailable";
            DockerfilePreviewBox.Text = _generatedRecipe.DockerfileContent;
        }

        private void ApplyModeUI()
        {
            SetButtonState(ConnectedModeButton, _mode == ToolConnectivityMode.Connected, true);
            SetButtonState(DisconnectedModeButton, _mode == ToolConnectivityMode.Disconnected, true);
            ConnectedStartPointsPanel.IsVisible = _mode == ToolConnectivityMode.Connected;
            DisconnectedStartPointsPanel.IsVisible = _mode == ToolConnectivityMode.Disconnected;
            ConnectedFiltersPanel.IsVisible = _mode == ToolConnectivityMode.Connected;
            DisconnectedFiltersPanel.IsVisible = _mode == ToolConnectivityMode.Disconnected;

            ModeHintText.Text = _mode == ToolConnectivityMode.Connected
                ? "외부 검색은 초안 생성을 돕는 단계입니다. 최종 등록과 실행은 내부 digest와 검증 evidence를 기준으로 고정됩니다."
                : "Disconnected 모드에서도 같은 authoring 흐름을 유지하되, 외부 호출 없이 내부 source에서만 시작합니다.";
        }

        private void ApplyRouteFilterUI()
        {
            SetButtonState(BiocondaFilterButton, _selectedRouteFilter == ToolSourceRoute.ExternalConda, true);
            SetButtonState(GitHubReleaseFilterButton, _selectedRouteFilter == ToolSourceRoute.ExternalGitHubRelease, true);
            SetButtonState(OciRegistryFilterButton, _selectedRouteFilter == ToolSourceRoute.ExternalOciRegistry, true);
            SetButtonState(AllExternalFilterButton, _selectedRouteFilter == null && _mode == ToolConnectivityMode.Connected, true);
            SetButtonState(InternalSeedFilterButton, _selectedRouteFilter == ToolSourceRoute.InternalSeed, true);
            SetButtonState(LocalMirrorFilterButton, _selectedRouteFilter == ToolSourceRoute.LocalPackageMirror, true);
            SetButtonState(RecipeBundleFilterButton, _selectedRouteFilter == ToolSourceRoute.RecipeBundle, true);
            SetButtonState(InternalRegistryFilterButton, _selectedRouteFilter == ToolSourceRoute.InternalOciRegistry, true);
        }

        private string GetExplorerHint()
        {
            if (_mode == ToolConnectivityMode.Connected)
            {
                return _selectedRouteFilter switch
                {
                    ToolSourceRoute.ExternalConda => "Bioconda package 후보를 실제로 조회합니다. 검색 결과는 ToolInstallRecipe로 정규화된 뒤 environment.yml과 multi-stage Dockerfile로 이어집니다.",
                    ToolSourceRoute.ExternalGitHubRelease => "GitHub Release route는 현재 fixture 기반 preview입니다. 이후 release metadata 수집과 source build generator가 붙을 자리입니다.",
                    ToolSourceRoute.ExternalOciRegistry => "OCI Registry route는 기존 image를 runtime 출발점으로 보는 흐름을 검증합니다.",
                    null => "외부 route 전체를 비교합니다. Bioconda, GitHub Release, OCI Registry 후보를 한 화면에서 비교할 수 있습니다.",
                    _ => string.Empty,
                };
            }

            return _selectedRouteFilter switch
            {
                ToolSourceRoute.InternalSeed => "내부 시드 레시피를 출발점으로 사용하는 흐름입니다.",
                ToolSourceRoute.LocalPackageMirror => "내부 패키지 미러를 사용하는 흐름입니다.",
                ToolSourceRoute.RecipeBundle => "반입된 recipe bundle을 사용하는 흐름입니다.",
                ToolSourceRoute.InternalOciRegistry => "내부 Harbor/Registry의 기존 이미지를 사용하는 흐름입니다.",
                _ => string.Empty,
            };
        }

        private string GetRouteFilterLabel()
        {
            return _selectedRouteFilter switch
            {
                ToolSourceRoute.ExternalConda => "Bioconda",
                ToolSourceRoute.ExternalGitHubRelease => "GitHub Release",
                ToolSourceRoute.ExternalOciRegistry => "OCI Registry",
                ToolSourceRoute.InternalSeed => "Internal Seed",
                ToolSourceRoute.LocalPackageMirror => "Local Mirror",
                ToolSourceRoute.RecipeBundle => "Recipe Bundle",
                ToolSourceRoute.InternalOciRegistry => "Internal Registry",
                null => _mode == ToolConnectivityMode.Connected ? "All External" : "Current Mode",
                _ => "Route",
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
            if (_mode == ToolConnectivityMode.Connected)
            {
                return await LoadConnectedCandidatesAsync(query).ConfigureAwait(true);
            }

            var route = _selectedRouteFilter ?? ToolSourceRoute.InternalSeed;
            return _toolSourceExplorerService
                .SearchCandidates(ToolConnectivityMode.Disconnected, _selectedStartPoint, query)
                .Where(candidate => candidate.Route == route)
                .ToList();
        }

        private async Task<IReadOnlyList<ToolSourceCandidate>> LoadConnectedCandidatesAsync(string query)
        {
            if (_selectedRouteFilter == ToolSourceRoute.ExternalConda)
            {
                return await _biocondaSearchService.SearchAsync(query).ConfigureAwait(true);
            }

            var fixtureCandidates = _toolSourceExplorerService
                .SearchCandidates(ToolConnectivityMode.Connected, ToolSourceStartPoint.ExternalSearch, query);

            if (_selectedRouteFilter == null)
            {
                var condaCandidates = await _biocondaSearchService.SearchAsync(query).ConfigureAwait(true);
                return condaCandidates
                    .Concat(fixtureCandidates.Where(candidate => candidate.Route != ToolSourceRoute.ExternalConda))
                    .ToList();
            }

            return fixtureCandidates
                .Where(candidate => candidate.Route == _selectedRouteFilter)
                .ToList();
        }

        private static string GetStartPointLabel(ToolSourceStartPoint startPoint)
        {
            return startPoint switch
            {
                ToolSourceStartPoint.InternalSeed => "내부 시드",
                ToolSourceStartPoint.LocalPackageMirror => "내부 미러",
                ToolSourceStartPoint.InternalOciRegistry => "내부 Registry",
                ToolSourceStartPoint.ExternalSearch => "외부 검색",
                ToolSourceStartPoint.RecipeBundle => "Recipe Bundle",
                _ => startPoint.ToString(),
            };
        }

        private void ApplyStepState(int activeStep)
        {
            Step1Badge.Background = new SolidColorBrush(Color.Parse(activeStep >= 1 ? "#2F67FF" : "#16253F"));
            Step2Badge.Background = new SolidColorBrush(Color.Parse(activeStep >= 2 ? "#2F67FF" : "#16253F"));
            Step3Badge.Background = new SolidColorBrush(Color.Parse(activeStep >= 3 ? "#2F67FF" : "#16253F"));
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
