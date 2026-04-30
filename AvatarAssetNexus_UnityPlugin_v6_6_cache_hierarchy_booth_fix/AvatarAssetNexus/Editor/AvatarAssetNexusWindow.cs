using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Networking;
using Object = UnityEngine.Object;

namespace AvatarAssetNexus
{
    public class AvatarAssetNexusWindow : EditorWindow
    {
        private enum TopTab
        {
            Dashboard,
            Library,
            AvatarSelect,
            PrefabList,
            Assets
        }

        private enum Language
        {
            Chinese,
            Japanese,
            English
        }

        private enum PrefabListMode
        {
            Referenced,
            Unused
        }

        private enum SortMode
        {
            Name,
            Size,
            Type,
            Date
        }

        private const string PrefLanguage = "VRAE_Lite_Language_v64";
        private const string PrefLibraryRoot = "VRAE_Lite_LibraryRoot";
        private const string PrefSearch = "VRAE_Lite_Search";
        private const string PrefListView = "VRAE_Lite_ListView";
        private const string PrefSortMode = "VRAE_Lite_SortMode";
        private const string PrefSortDescending = "VRAE_Lite_SortDescending";
        private const string PrefSelectedCategory = "VRAE_Lite_SelectedCategory";
        private const string PrefLastAvatarKey = "VRAE_Lite_LastAvatarKey";
        private const string PrefRecentImportCategory = "VRAE_Lite_RecentImportCategory";
        private const string PrefDashboardAssetFolderFoldout = "VRAE_Lite_DashboardAssetFolderFoldout";
        private const string PrefDashboardCommonFoldout = "VRAE_Lite_DashboardCommonFoldout";
        private const string PrefStrictAvatarInstanceSelection = "VRAE_Nexus_StrictAvatarInstanceSelection";

        private AvatarAssetNexusDatabase _db;
        private TopTab _activeTab = TopTab.Dashboard;
        private Language _language = Language.Chinese;
        private string _libraryRoot = "Assets/_MyVRProject/Purchased";
        private string _search = string.Empty;
        private bool _listView;
        private SortMode _sortMode = SortMode.Name;
        private bool _sortDescending;
        private Vector2 _leftScroll;
        private Vector2 _mainScroll;
        private Vector2 _rightScroll;
        private Vector2 _assetScroll;

        private Object _currentAvatarObject;
        private string _currentAvatarKey;
        private string _currentAvatarDisplayName;
        private string _currentAvatarPath;
        private AvatarPointerCache _currentCache;
        private PrefabListMode _prefabListMode = PrefabListMode.Referenced;
        private List<PrefabPointerEntry> _unusedPrefabEntries = new List<PrefabPointerEntry>();
        private string _selectedPrefabPath;
        private string _selectedAssetPath;
        private string _selectedCategoryId;
        private string _recentImportCategoryId;
        private string _renamingCategoryId;
        private string _categoryRenameDraft;
        private bool _isFetchingBooth;
        private bool _renameFocusPending;
        private string _statusMessage = string.Empty;
        private double _statusUntil;

        private readonly HashSet<string> _selectedPaths = new HashSet<string>();
        private string _lastClickedPath;
        private string _dragSourcePath;
        private Vector2 _dragStartMousePosition;
        private List<string> _currentVisiblePaths = new List<string>();

        private readonly Dictionary<string, Texture2D> _previewCache = new Dictionary<string, Texture2D>();
        private readonly Dictionary<string, Texture2D> _remotePreviewCache = new Dictionary<string, Texture2D>();
        private readonly HashSet<string> _remotePreviewRequests = new HashSet<string>();
        private readonly Dictionary<string, bool> _foldouts = new Dictionary<string, bool>();
        private bool _dashboardAssetFolderExpanded = true;
        private bool _dashboardCommonExpanded = false;
        private bool _strictAvatarInstanceSelection = true;
        private string _categoryDragSourceId;
        private Vector2 _categoryDragStartMousePosition;

        [MenuItem("Tools/Avatar Asset Nexus/Open")]
        public static void Open()
        {
            var window = GetWindow<AvatarAssetNexusWindow>();
            window.titleContent = new GUIContent("Avatar Asset Nexus");
            window.minSize = new Vector2(1100, 620);
            window.Show();
        }

        private void OnEnable()
        {
            _db = AvatarAssetNexusDatabase.LoadOrCreate();
            _language = (Language)EditorPrefs.GetInt(PrefLanguage, (int)Language.English);
            _libraryRoot = EditorPrefs.GetString(PrefLibraryRoot, "Assets/_MyVRProject/Purchased");
            _search = EditorPrefs.GetString(PrefSearch, string.Empty);
            _listView = EditorPrefs.GetBool(PrefListView, false);
            _sortMode = (SortMode)EditorPrefs.GetInt(PrefSortMode, (int)SortMode.Name);
            _sortDescending = EditorPrefs.GetBool(PrefSortDescending, false);
            AssetPreview.SetPreviewTextureCacheSize(512);
            _selectedCategoryId = EditorPrefs.GetString(PrefSelectedCategory, _db.customCategories.Count > 0 ? _db.customCategories[0].id : string.Empty);
            _recentImportCategoryId = EditorPrefs.GetString(PrefRecentImportCategory, _selectedCategoryId);
            _dashboardAssetFolderExpanded = EditorPrefs.GetBool(PrefDashboardAssetFolderFoldout, true);
            _dashboardCommonExpanded = EditorPrefs.GetBool(PrefDashboardCommonFoldout, false);
            _strictAvatarInstanceSelection = EditorPrefs.GetBool(PrefStrictAvatarInstanceSelection, true);

            var lastAvatarKey = EditorPrefs.GetString(PrefLastAvatarKey, string.Empty);
            if (!string.IsNullOrEmpty(lastAvatarKey))
            {
                _currentCache = _db.FindAvatarCache(lastAvatarKey);
                if (_currentCache != null)
                {
                    _currentAvatarKey = _currentCache.avatarKey;
                    _currentAvatarDisplayName = _currentCache.avatarDisplayName;
                    _currentAvatarPath = _currentCache.avatarAssetPath;
                }
            }
        }

        private void OnDisable()
        {
            SaveEditorPrefs();
            if (_db != null) _db.Save();
        }

        private void SaveEditorPrefs()
        {
            EditorPrefs.SetInt(PrefLanguage, (int)_language);
            EditorPrefs.SetString(PrefLibraryRoot, _libraryRoot ?? string.Empty);
            EditorPrefs.SetString(PrefSearch, _search ?? string.Empty);
            EditorPrefs.SetBool(PrefListView, _listView);
            EditorPrefs.SetInt(PrefSortMode, (int)_sortMode);
            EditorPrefs.SetBool(PrefSortDescending, _sortDescending);
            EditorPrefs.SetString(PrefSelectedCategory, _selectedCategoryId ?? string.Empty);
            EditorPrefs.SetString(PrefLastAvatarKey, _currentAvatarKey ?? string.Empty);
            EditorPrefs.SetString(PrefRecentImportCategory, _recentImportCategoryId ?? string.Empty);
            EditorPrefs.SetBool(PrefDashboardAssetFolderFoldout, _dashboardAssetFolderExpanded);
            EditorPrefs.SetBool(PrefDashboardCommonFoldout, _dashboardCommonExpanded);
            EditorPrefs.SetBool(PrefStrictAvatarInstanceSelection, _strictAvatarInstanceSelection);
        }

        private void OnGUI()
        {
            if (_db == null) _db = AvatarAssetNexusDatabase.LoadOrCreate();
            _db.EnsureDefaults();

            DrawTopBar();
            DrawTabs();
            DrawStatusBar();

            EditorGUILayout.BeginHorizontal();
            DrawLeftSidebar(250f);
            DrawMainPanel();
            DrawRightDetails(330f);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawTopBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar, GUILayout.Height(30));
            GUILayout.Label("Avatar Asset Nexus", EditorStyles.boldLabel, GUILayout.Width(145));

            GUILayout.Label(T("search"), GUILayout.Width(44));
            EditorGUI.BeginChangeCheck();
            _search = GUILayout.TextField(_search ?? string.Empty, GUI.skin.FindStyle("ToolbarSeachTextField") ?? EditorStyles.toolbarTextField, GUILayout.MinWidth(240));
            if (EditorGUI.EndChangeCheck()) SaveEditorPrefs();

            if (GUILayout.Button(T("clear"), EditorStyles.toolbarButton, GUILayout.Width(54)))
            {
                _search = string.Empty;
                SaveEditorPrefs();
                GUI.FocusControl(null);
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label(T("language"), GUILayout.Width(70));
            EditorGUI.BeginChangeCheck();
            _language = (Language)EditorGUILayout.EnumPopup(_language, GUILayout.Width(92));
            if (EditorGUI.EndChangeCheck()) SaveEditorPrefs();

            DrawViewAndSortControls();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawViewAndSortControls()
        {
            GUILayout.Label(T("view"), GUILayout.Width(38));
            var nextListView = GUILayout.Toggle(_listView, _listView ? T("listView") : T("gridView"), EditorStyles.toolbarButton, GUILayout.Width(90));
            if (nextListView != _listView)
            {
                _listView = nextListView;
                SaveEditorPrefs();
            }

            GUILayout.Label(T("sort"), GUILayout.Width(38));
            EditorGUI.BeginChangeCheck();
            var sortLabels = GetSortLabels();
            _sortMode = (SortMode)EditorGUILayout.Popup((int)_sortMode, sortLabels, GUILayout.Width(90));
            if (EditorGUI.EndChangeCheck())
            {
                SaveEditorPrefs();
            }

            if (GUILayout.Button(_sortDescending ? "↓" : "↑", EditorStyles.toolbarButton, GUILayout.Width(28)))
            {
                _sortDescending = !_sortDescending;
                SaveEditorPrefs();
            }
        }

        private string[] GetSortLabels()
        {
            return new[]
            {
                T("sortName"),
                T("sortSize"),
                T("sortType"),
                T("sortDate")
            };
        }

        private void DrawTabs()
        {
            var labels = new[]
            {
                T("tabDashboard"),
                T("tabLibrary"),
                T("tabAvatarSelect"),
                T("tabPrefabList"),
                T("tabAssets")
            };

            if ((int)_activeTab < 0 || (int)_activeTab >= labels.Length) _activeTab = TopTab.Dashboard;
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(6);
            _activeTab = (TopTab)GUILayout.Toolbar((int)_activeTab, labels, GUILayout.Height(28));
            GUILayout.Space(6);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawStatusBar()
        {
            if (string.IsNullOrEmpty(_statusMessage) || EditorApplication.timeSinceStartup > _statusUntil)
            {
                return;
            }

            var previous = GUI.color;
            GUI.color = new Color(0.75f, 0.95f, 1f, 1f);
            EditorGUILayout.HelpBox(_statusMessage, MessageType.Info);
            GUI.color = previous;
        }

        private void DrawLeftSidebar(float width)
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(width));
            _leftScroll = EditorGUILayout.BeginScrollView(_leftScroll);

            DrawWorkflowBox();
            GUILayout.Space(6);
            DrawCustomCategories();
            GUILayout.Space(6);
            DrawRecentAvatars();
            GUILayout.Space(6);
            DrawFiltersSummary();

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawWorkflowBox()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label(T("workflow"), EditorStyles.boldLabel);
            GUILayout.Label(T("workflowLines"), EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndVertical();
        }

        private void DrawCustomCategories()
        {
            var headerRect = GUILayoutUtility.GetRect(1, 24, GUILayout.ExpandWidth(true));
            GUI.Label(headerRect, T("customCategories"), EditorStyles.boldLabel);
            HandleCategoryHeaderContext(headerRect);

            var createRect = new Rect(headerRect.xMax - 24, headerRect.y + 2, 22, 20);
            if (GUI.Button(createRect, "+"))
            {
                var category = _db.CreateCategory(T("newCategory"));
                _selectedCategoryId = category.id;
                _db.Save();
                SaveEditorPrefs();
            }

            foreach (var category in _db.customCategories.ToList())
            {
                DrawCategoryRow(category);
            }
        }

        private void HandleCategoryHeaderContext(Rect rect)
        {
            var e = Event.current;
            if (e.type != EventType.ContextClick || !rect.Contains(e.mousePosition)) return;
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent(T("createCategory")), false, () =>
            {
                var category = _db.CreateCategory(T("newCategory"));
                _selectedCategoryId = category.id;
                _db.Save();
                SaveEditorPrefs();
            });
            menu.ShowAsContext();
            e.Use();
        }

        private void DrawCategoryRow(CustomCategoryRecord category)
        {
            if (category == null) return;
            var count = category.assetPaths == null ? 0 : category.assetPaths.Count;
            var rect = GUILayoutUtility.GetRect(1, 24, GUILayout.ExpandWidth(true));
            var selected = _selectedCategoryId == category.id;

            if (selected)
            {
                EditorGUI.DrawRect(rect, new Color(0.22f, 0.33f, 0.52f, 0.85f));
            }
            else if (rect.Contains(Event.current.mousePosition))
            {
                EditorGUI.DrawRect(rect, new Color(1f, 1f, 1f, 0.06f));
            }

            if (_renamingCategoryId == category.id)
            {
                GUI.SetNextControlName("renameCategory" + category.id);
                EditorGUI.BeginChangeCheck();
                _categoryRenameDraft = EditorGUI.DelayedTextField(rect, _categoryRenameDraft ?? category.name);
                if (EditorGUI.EndChangeCheck())
                {
                    CommitCategoryRename(category);
                }
                if (_renameFocusPending)
                {
                    _categoryRenameDraft = string.IsNullOrEmpty(_categoryRenameDraft) ? category.name : _categoryRenameDraft;
                    EditorGUI.FocusTextInControl("renameCategory" + category.id);
                    _renameFocusPending = false;
                }

                var e = Event.current;
                if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
                {
                    _renamingCategoryId = null;
                    _categoryRenameDraft = string.Empty;
                    GUI.FocusControl(null);
                    e.Use();
                }
            }
            else
            {
                GUI.Label(new Rect(rect.x + 7, rect.y + 4, rect.width - 12, rect.height - 4), category.name + "  (" + count + ")");
            }

            HandleCategoryDrop(rect, category);
            HandleCategoryMouse(rect, category);
        }

        private void CommitCategoryRename(CustomCategoryRecord category)
        {
            if (category == null) return;
            var nextName = (_categoryRenameDraft ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(nextName)) nextName = T("newCategory");
            category.name = nextName;
            _renamingCategoryId = null;
            _categoryRenameDraft = string.Empty;
            GUI.FocusControl(null);
            _db.Save();
            Repaint();
        }

        private void HandleCategoryMouse(Rect rect, CustomCategoryRecord category)
        {
            var e = Event.current;
            if (!rect.Contains(e.mousePosition)) return;

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                _selectedCategoryId = category.id;
                _activeTab = TopTab.Library;
                _categoryDragSourceId = category.id;
                _categoryDragStartMousePosition = e.mousePosition;
                SaveEditorPrefs();
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && e.button == 0 && _categoryDragSourceId == category.id)
            {
                if ((e.mousePosition - _categoryDragStartMousePosition).sqrMagnitude > 16f)
                {
                    StartCategoryDrag(category);
                    e.Use();
                }
            }
            else if (e.type == EventType.ContextClick)
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent(T("createCategory")), false, () =>
                {
                    var created = _db.CreateCategory(T("newCategory"));
                    _selectedCategoryId = created.id;
                    _db.Save();
                    SaveEditorPrefs();
                });
                menu.AddItem(new GUIContent(T("rename")), false, () =>
                {
                    _renamingCategoryId = category.id;
                    _categoryRenameDraft = category.name;
                    _renameFocusPending = true;
                    Repaint();
                });
                menu.AddSeparator("");
                menu.AddItem(new GUIContent(T("addSelectedAsset")), false, () => AddSelectedResourceToCategory(category.id));
                menu.AddItem(new GUIContent(T("addPrefabTree")), false, () => AddSelectedPrefabTreeToCategory(category.id));
                menu.AddSeparator("");
                menu.AddItem(new GUIContent(T("deleteCategory")), false, () =>
                {
                    if (EditorUtility.DisplayDialog(T("deleteCategory"), T("deleteCategoryConfirm"), T("delete"), T("cancel")))
                    {
                        _db.customCategories.Remove(category);
                        if (_selectedCategoryId == category.id)
                        {
                            _selectedCategoryId = _db.customCategories.Count > 0 ? _db.customCategories[0].id : string.Empty;
                        }
                        _db.Save();
                        SaveEditorPrefs();
                    }
                });
                menu.ShowAsContext();
                e.Use();
            }
        }

        private void HandleCategoryDrop(Rect rect, CustomCategoryRecord category)
        {
            var e = Event.current;
            if (!rect.Contains(e.mousePosition)) return;
            if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform) return;

            var draggedCategoryId = DragAndDrop.GetGenericData("VRAE_CategoryId") as string;
            if (!string.IsNullOrEmpty(draggedCategoryId) && draggedCategoryId != category.id)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Move;
                if (e.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    var placeAfter = e.mousePosition.y > rect.center.y;
                    _db.MoveCategory(draggedCategoryId, category.id, placeAfter);
                    _db.Save();
                    SetStatus(T("categoryOrderUpdated"));
                    _categoryDragSourceId = null;
                }
                e.Use();
                return;
            }

            var paths = DragAndDrop.objectReferences
                .Select(AssetDatabase.GetAssetPath)
                .Where(IsProjectPath)
                .Distinct()
                .ToList();

            DragAndDrop.visualMode = paths.Count > 0 ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
            if (e.type == EventType.DragPerform && paths.Count > 0)
            {
                DragAndDrop.AcceptDrag();
                _db.AddAssetsToCategory(category.id, paths);
                _db.Save();
                SetStatus(string.Format(T("addedToCategory"), paths.Count, category.name));
            }
            e.Use();
        }

        private void StartCategoryDrag(CustomCategoryRecord category)
        {
            if (category == null) return;
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.SetGenericData("VRAE_CategoryId", category.id);
            DragAndDrop.objectReferences = new Object[0];
            DragAndDrop.paths = new string[0];
            DragAndDrop.StartDrag(category.name);
        }

        private void DrawRecentAvatars()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label(T("recentAvatars"), EditorStyles.boldLabel);
            if (_db.avatarCaches.Count == 0)
            {
                GUILayout.Label(T("noCachedAvatar"), EditorStyles.wordWrappedMiniLabel);
            }
            else
            {
                foreach (var cache in _db.avatarCaches.OrderByDescending(x => x.analyzedAt).Take(8))
                {
                    var display = string.IsNullOrEmpty(cache.avatarDisplayName) ? cache.avatarKey : cache.avatarDisplayName;
                    if (GUILayout.Button(display + "  (" + cache.prefabs.Count + ")", EditorStyles.miniButton))
                    {
                        _currentCache = cache;
                        _currentAvatarKey = cache.avatarKey;
                        _currentAvatarDisplayName = cache.avatarDisplayName;
                        _currentAvatarPath = cache.avatarAssetPath;
                        if (!string.IsNullOrEmpty(cache.autoCategoryId)) _selectedCategoryId = cache.autoCategoryId;
                        _unusedPrefabEntries = cache.unusedPrefabs == null ? new List<PrefabPointerEntry>() : cache.unusedPrefabs.ToList();
                        _prefabListMode = PrefabListMode.Referenced;
                        _activeTab = TopTab.PrefabList;
                        SaveEditorPrefs();
                    }
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawFiltersSummary()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label(T("quickFilters"), EditorStyles.boldLabel);
            GUILayout.Label(T("selectedCategory") + ": " + GetSelectedCategoryName(), EditorStyles.wordWrappedMiniLabel);
            GUILayout.Label(T("currentAvatar") + ": " + (string.IsNullOrEmpty(_currentAvatarDisplayName) ? T("none") : _currentAvatarDisplayName), EditorStyles.wordWrappedMiniLabel);
            GUILayout.Label(T("search") + ": " + (string.IsNullOrEmpty(_search) ? T("none") : _search), EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndVertical();
        }

        private void DrawMainPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            _mainScroll = EditorGUILayout.BeginScrollView(_mainScroll);

            switch (_activeTab)
            {
                case TopTab.Dashboard:
                    DrawDashboard();
                    break;
                case TopTab.Library:
                    DrawLibrary();
                    break;
                case TopTab.AvatarSelect:
                    DrawAvatarSelect();
                    break;
                case TopTab.PrefabList:
                    DrawPrefabList();
                    break;
                case TopTab.Assets:
                    DrawAssetsTab();
                    break;
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawDashboard()
        {
            GUILayout.Label(T("dashboardTitle"), EditorStyles.boldLabel);
            GUILayout.Label(T("dashboardSubtitle"), EditorStyles.wordWrappedLabel);
            GUILayout.Space(8);

            var cachedAvatarCount = _db.avatarCaches.Count;
            var cachedPrefabCount = _db.avatarCaches.SelectMany(x => x.prefabs).Select(x => x.prefabPath).Distinct().Count();
            var categoryCount = _db.customCategories.Count;
            var categorizedAssetCount = _db.customCategories.SelectMany(x => x.assetPaths ?? new List<string>()).Distinct().Count();

            EditorGUILayout.BeginHorizontal();
            DrawStatCard(T("cachedAvatars"), cachedAvatarCount.ToString());
            DrawStatCard(T("cachedPrefabs"), cachedPrefabCount.ToString());
            DrawStatCard(T("customCategories"), categoryCount.ToString());
            DrawStatCard(T("categorizedAssets"), categorizedAssetCount.ToString());
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(12);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(T("goAvatarSelect"), GUILayout.Height(42))) _activeTab = TopTab.AvatarSelect;
            if (GUILayout.Button(T("goLibrary"), GUILayout.Height(42))) _activeTab = TopTab.Library;
            if (GUILayout.Button(T("createCategory"), GUILayout.Height(42)))
            {
                var category = _db.CreateCategory(T("newCategory"));
                _selectedCategoryId = category.id;
                _db.Save();
                _activeTab = TopTab.Library;
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(8);
            DrawLibraryRootControls();
            DrawRecentImportAssignBox();
            DrawDashboardDatabaseControls();

            GUILayout.Space(12);
            GUILayout.Label(T("recentAnalysis"), EditorStyles.boldLabel);
            foreach (var cache in _db.avatarCaches.OrderByDescending(x => x.analyzedAt).Take(6))
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                var avatarIcon = GetMiniIconForPath(cache.avatarAssetPath);
                if (avatarIcon != null) GUILayout.Label(avatarIcon, GUILayout.Width(24), GUILayout.Height(24));
                else GUILayout.Label(string.Empty, GUILayout.Width(24), GUILayout.Height(24));
                EditorGUILayout.BeginVertical();
                GUILayout.Label(cache.avatarDisplayName, EditorStyles.boldLabel);
                GUILayout.Label(string.Format(T("cacheSummary"), cache.prefabs.Count, cache.analyzedAt), EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
                if (GUILayout.Button(T("open"), GUILayout.Width(70)))
                {
                    _currentCache = cache;
                    _currentAvatarKey = cache.avatarKey;
                    _currentAvatarDisplayName = cache.avatarDisplayName;
                    _currentAvatarPath = cache.avatarAssetPath;
                    _prefabListMode = PrefabListMode.Referenced;
                    _activeTab = TopTab.PrefabList;
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawStatCard(string label, string value)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Height(72));
            GUILayout.Label(label, EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            GUILayout.Label(value, EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndVertical();
        }

        private void DrawLibrary()
        {
            GUILayout.Label(T("libraryTitle"), EditorStyles.boldLabel);
            GUILayout.Label(T("librarySubtitle"), EditorStyles.wordWrappedMiniLabel);
            GUILayout.Space(8);

            var paths = GetLibraryPaths().Where(MatchesSearch).Distinct().ToList();
            DrawAssetCollection(paths, false);
        }

        private void DrawDashboardDatabaseControls()
        {
            _dashboardCommonExpanded = EditorGUILayout.Foldout(_dashboardCommonExpanded, T("dashboardSettings"), true);
            SaveEditorPrefs();
            if (!_dashboardCommonExpanded) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(T("pingDataAsset"), GUILayout.Width(130)))
            {
                EditorGUIUtility.PingObject(_db);
                Selection.activeObject = _db;
            }
            GUILayout.Label(T("settingsNote"), EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndHorizontal();
            EditorGUI.BeginChangeCheck();
            _strictAvatarInstanceSelection = EditorGUILayout.ToggleLeft(T("strictAvatarInstanceSelection"), _strictAvatarInstanceSelection);
            if (EditorGUI.EndChangeCheck()) SaveEditorPrefs();
            if (GUILayout.Button(T("resetDashboardSettings"), GUILayout.Width(160)))
            {
                ConfirmAndResetAllPersistentData();
            }
            EditorGUILayout.EndVertical();
        }

        private void ConfirmAndResetAllPersistentData()
        {
            if (!EditorUtility.DisplayDialog(T("resetDashboardSettings"), T("resetAllConfirm"), T("resetConfirmButton"), T("cancel")))
            {
                return;
            }

            ResetAllPersistentData();
        }

        private void ResetAllPersistentData()
        {
            EditorPrefs.DeleteKey(PrefLanguage);
            EditorPrefs.DeleteKey(PrefLibraryRoot);
            EditorPrefs.DeleteKey(PrefSearch);
            EditorPrefs.DeleteKey(PrefListView);
            EditorPrefs.DeleteKey(PrefSortMode);
            EditorPrefs.DeleteKey(PrefSortDescending);
            EditorPrefs.DeleteKey(PrefSelectedCategory);
            EditorPrefs.DeleteKey(PrefLastAvatarKey);
            EditorPrefs.DeleteKey(PrefRecentImportCategory);
            EditorPrefs.DeleteKey(PrefDashboardAssetFolderFoldout);
            EditorPrefs.DeleteKey(PrefDashboardCommonFoldout);
            EditorPrefs.DeleteKey(PrefStrictAvatarInstanceSelection);

            _language = Language.English;
            _libraryRoot = "Assets/_MyVRProject/Purchased";
            _search = string.Empty;
            _listView = false;
            _sortMode = SortMode.Name;
            _sortDescending = false;
            _selectedCategoryId = string.Empty;
            _recentImportCategoryId = string.Empty;
            _currentAvatarKey = string.Empty;
            _currentAvatarDisplayName = string.Empty;
            _currentAvatarPath = string.Empty;
            _currentAvatarObject = null;
            _currentCache = null;
            _selectedPrefabPath = string.Empty;
            _selectedAssetPath = string.Empty;
            _selectedPaths.Clear();
            _lastClickedPath = string.Empty;
            _prefabListMode = PrefabListMode.Referenced;
            _unusedPrefabEntries = new List<PrefabPointerEntry>();
            _strictAvatarInstanceSelection = true;
            _dashboardAssetFolderExpanded = true;
            _dashboardCommonExpanded = false;
            _previewCache.Clear();
            _remotePreviewCache.Clear();
            _remotePreviewRequests.Clear();
            _foldouts.Clear();

            if (_db != null)
            {
                _db.avatarCaches = new List<AvatarPointerCache>();
                _db.customCategories = new List<CustomCategoryRecord>();
                _db.assetMetadata = new List<AssetExtraMetadata>();
                _db.recentImportedAssetPaths = new List<string>();
                _db.recentImportedAt = string.Empty;
                _db.EnsureDefaults();
                _db.Save();
            }

            SaveEditorPrefs();
            SetStatus(T("dashboardSettingsReset"));
        }

        private void DrawLibraryRootControls()
        {
            _dashboardAssetFolderExpanded = EditorGUILayout.Foldout(_dashboardAssetFolderExpanded, T("assetFolderSettings"), true);
            SaveEditorPrefs();
            if (!_dashboardAssetFolderExpanded)
            {
                GUILayout.Space(4);
                return;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUI.BeginChangeCheck();
            _libraryRoot = EditorGUILayout.TextField(T("libraryRoot"), _libraryRoot);
            if (EditorGUI.EndChangeCheck()) SaveEditorPrefs();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(T("useSelectedFolder"), GUILayout.Width(150)))
            {
                var selected = Selection.activeObject;
                var path = selected == null ? string.Empty : AssetDatabase.GetAssetPath(selected);
                if (AssetDatabase.IsValidFolder(path))
                {
                    _libraryRoot = path;
                    SaveEditorPrefs();
                }
                else
                {
                    SetStatus(T("selectFolderFirst"));
                }
            }
            GUILayout.Label(T("libraryRootHint"), EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            GUILayout.Space(6);
        }

        private void DrawRecentImportAssignBox()
        {
            if (_db.recentImportedAssetPaths == null || _db.recentImportedAssetPaths.Count == 0) return;

            var paths = _db.recentImportedAssetPaths.Where(IsProjectPath).Distinct().ToList();
            if (paths.Count == 0) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label(T("recentImportDetected"), EditorStyles.boldLabel);
            GUILayout.Label(string.Format(T("recentImportSummary"), paths.Count, string.IsNullOrEmpty(_db.recentImportedAt) ? "-" : _db.recentImportedAt), EditorStyles.wordWrappedMiniLabel);

            var names = _db.customCategories.Select(x => x.name).ToArray();
            if (names.Length == 0)
            {
                if (GUILayout.Button(T("createCategory")))
                {
                    var category = _db.CreateCategory(T("newCategory"));
                    _selectedCategoryId = category.id;
                    _recentImportCategoryId = category.id;
                    _db.Save();
                    SaveEditorPrefs();
                }
            }
            else
            {
                if (string.IsNullOrEmpty(_recentImportCategoryId) || _db.GetCategory(_recentImportCategoryId) == null)
                {
                    _recentImportCategoryId = _selectedCategoryId;
                }
                var currentIndex = Mathf.Max(0, _db.customCategories.FindIndex(x => x.id == _recentImportCategoryId));
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(T("targetCategory"), GUILayout.Width(92));
                var nextIndex = EditorGUILayout.Popup(currentIndex, names);
                _recentImportCategoryId = _db.customCategories[nextIndex].id;
                if (GUILayout.Button(T("addImportedToCategory"), GUILayout.Width(150)))
                {
                    _db.AddAssetsToCategory(_recentImportCategoryId, paths);
                    _selectedCategoryId = _recentImportCategoryId;
                    _db.ClearRecentImportedAssets();
                    _db.Save();
                    SaveEditorPrefs();
                    SetStatus(string.Format(T("addedToCategory"), paths.Count, GetSelectedCategoryName()));
                }
                if (GUILayout.Button(T("ignore"), GUILayout.Width(70)))
                {
                    _db.ClearRecentImportedAssets();
                    _db.Save();
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();
            GUILayout.Space(6);
        }

        private IEnumerable<string> GetLibraryPaths()
        {
            var category = _db.GetCategory(_selectedCategoryId);
            if (category != null)
            {
                return (category.assetPaths ?? new List<string>()).Where(IsProjectPath);
            }

            var set = new HashSet<string>();
            foreach (var cache in _db.avatarCaches)
            {
                foreach (var prefab in cache.prefabs)
                {
                    if (IsProjectPath(prefab.prefabPath)) set.Add(prefab.prefabPath);
                    foreach (var dep in prefab.dependencyPaths ?? new List<string>())
                    {
                        if (IsProjectPath(dep)) set.Add(dep);
                    }
                }
            }

            foreach (var cat in _db.customCategories)
            {
                foreach (var path in cat.assetPaths ?? new List<string>())
                {
                    if (IsProjectPath(path)) set.Add(path);
                }
            }
            return set;
        }

        private void DrawAvatarSelect()
        {
            GUILayout.Label(T("avatarSelectTitle"), EditorStyles.boldLabel);
            GUILayout.Label(T("avatarSelectSubtitle"), EditorStyles.wordWrappedLabel);
            GUILayout.Space(8);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label(T("step1SelectAvatar"), EditorStyles.boldLabel);
            DrawAvatarObjectField();
            DrawCurrentAvatarInfo();
            GUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            GUI.enabled = HasCurrentAvatarSelection();
            if (GUILayout.Button(T("searchCurrentAvatarPrefabs"), GUILayout.Height(34)))
            {
                AnalyzeCurrentAvatar();
            }
            GUI.enabled = _currentCache != null;
            if (GUILayout.Button(T("openCachedPrefabList"), GUILayout.Height(34), GUILayout.Width(180)))
            {
                _prefabListMode = PrefabListMode.Referenced;
                _activeTab = TopTab.PrefabList;
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            GUILayout.Space(8);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label(T("unusedTitle"), EditorStyles.boldLabel);
            GUILayout.Label(T("unusedExplain"), EditorStyles.wordWrappedMiniLabel);
            GUI.enabled = _currentCache != null;
            if (GUILayout.Button(T("showUnusedPrefabs"), GUILayout.Height(32)))
            {
                BuildUnusedPrefabsForCurrentAvatar();
                _prefabListMode = PrefabListMode.Unused;
                _activeTab = TopTab.PrefabList;
            }
            GUI.enabled = true;
            EditorGUILayout.EndVertical();
        }

        private void DrawAvatarObjectField()
        {
            EditorGUI.BeginChangeCheck();
            var picked = EditorGUILayout.ObjectField(T("avatarField"), _currentAvatarObject, typeof(Object), true);
            if (EditorGUI.EndChangeCheck())
            {
                if (picked == null)
                {
                    SetCurrentAvatar(null);
                }
                else if (IsValidAvatarObject(picked))
                {
                    SetCurrentAvatar(picked);
                }
                else
                {
                    SetStatus(T("invalidAvatarIgnored"));
                }
            }
        }

        private void DrawAvatarDropBox()
        {
            var rect = GUILayoutUtility.GetRect(1, 78, GUILayout.ExpandWidth(true));
            var previousColor = GUI.color;
            GUI.color = rect.Contains(Event.current.mousePosition) ? new Color(0.65f, 0.85f, 1f, 1f) : Color.white;
            GUI.Box(rect, string.IsNullOrEmpty(_currentAvatarDisplayName)
                ? T("dragAvatarHere")
                : string.Format(T("selectedAvatarBox"), _currentAvatarDisplayName), EditorStyles.helpBox);
            GUI.color = previousColor;

            var e = Event.current;
            if (!rect.Contains(e.mousePosition)) return;
            if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform) return;

            var valid = DragAndDrop.objectReferences.FirstOrDefault(IsValidAvatarObject);
            DragAndDrop.visualMode = valid != null ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;

            if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                if (valid != null)
                {
                    SetCurrentAvatar(valid);
                    SetStatus(T("avatarSelected"));
                }
                else
                {
                    SetStatus(T("invalidAvatarIgnored"));
                }
            }
            e.Use();
        }

        private void DrawCurrentAvatarInfo()
        {
            GUILayout.Space(4);
            var rect = GUILayoutUtility.GetRect(1, 96, GUILayout.ExpandWidth(true));
            var isHover = rect.Contains(Event.current.mousePosition);
            var previousColor = GUI.color;
            GUI.color = isHover ? new Color(0.70f, 0.88f, 1f, 1f) : Color.white;
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
            GUI.color = previousColor;

            var previewRect = new Rect(rect.x + 8, rect.y + 8, 80, 80);
            var avatarPreview = GetAvatarPreviewTexture();
            if (avatarPreview != null) GUI.DrawTexture(previewRect, avatarPreview, ScaleMode.ScaleToFit);
            else GUI.Label(previewRect, "Avatar", CenteredMiniLabel());

            var infoX = previewRect.xMax + 10;
            var infoWidth = rect.width - 102;
            var title = string.IsNullOrEmpty(_currentAvatarDisplayName) ? T("dragAvatarHere") : _currentAvatarDisplayName;
            GUI.Label(new Rect(infoX, rect.y + 8, infoWidth, 18), title, EditorStyles.boldLabel);

            var pathText = string.IsNullOrEmpty(_currentAvatarPath) ? T("sceneOrUnknown") : _currentAvatarPath;
            GUI.Label(new Rect(infoX, rect.y + 29, infoWidth, 18), pathText, EditorStyles.miniLabel);

            var cacheText = _currentCache != null
                ? string.Format(T("cacheSummary"), _currentCache.prefabs.Count, _currentCache.analyzedAt)
                : T("noCacheYet");
            GUI.Label(new Rect(infoX, rect.y + 50, infoWidth, 18), cacheText, EditorStyles.miniLabel);
            GUI.Label(new Rect(infoX, rect.y + 70, infoWidth, 18), T("avatarCardHint"), EditorStyles.miniLabel);

            HandleAvatarInfoCardInput(rect);
        }

        private Texture2D GetAvatarPreviewTexture()
        {
            if (IsProjectPath(_currentAvatarPath)) return GetBestPreview(_currentAvatarPath, true);
            if (_currentAvatarObject != null)
            {
                var preview = AssetPreview.GetAssetPreview(_currentAvatarObject);
                if (preview != null) return preview;
                preview = AssetPreview.GetMiniThumbnail(_currentAvatarObject);
                if (preview != null) return preview;
            }
            return null;
        }

        private void HandleAvatarInfoCardInput(Rect rect)
        {
            var e = Event.current;
            if (!rect.Contains(e.mousePosition)) return;

            if (e.type == EventType.DragUpdated || e.type == EventType.DragPerform)
            {
                var valid = DragAndDrop.objectReferences.FirstOrDefault(IsValidAvatarObject);
                DragAndDrop.visualMode = valid != null ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
                if (e.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    if (valid != null)
                    {
                        SetCurrentAvatar(valid);
                        SetStatus(T("avatarSelected"));
                    }
                    else
                    {
                        SetStatus(T("invalidAvatarIgnored"));
                    }
                }
                e.Use();
                return;
            }

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                SelectCurrentAvatarAsset();
                _dragSourcePath = _currentAvatarPath;
                _dragStartMousePosition = e.mousePosition;
                e.Use();
                return;
            }

            if (e.type == EventType.MouseDrag && e.button == 0 && IsProjectPath(_currentAvatarPath))
            {
                if ((e.mousePosition - _dragStartMousePosition).sqrMagnitude > 16f)
                {
                    StartProjectDrag(_currentAvatarPath);
                    e.Use();
                }
                return;
            }

            if (e.type == EventType.ContextClick)
            {
                if (IsProjectPath(_currentAvatarPath))
                {
                    SelectCurrentAvatarAsset();
                    ShowAssetContextMenu(_currentAvatarPath);
                }
                e.Use();
            }
        }

        private void SelectCurrentAvatarAsset()
        {
            if (!IsProjectPath(_currentAvatarPath))
            {
                if (_currentAvatarObject != null)
                {
                    Selection.activeObject = _currentAvatarObject;
                    EditorGUIUtility.PingObject(_currentAvatarObject);
                }
                return;
            }

            _selectedPaths.Clear();
            _selectedPaths.Add(_currentAvatarPath);
            _lastClickedPath = _currentAvatarPath;
            if (IsPrefab(_currentAvatarPath)) SelectPrefabOnly(_currentAvatarPath);
            else SelectAsset(_currentAvatarPath);
            SelectInInspector(_currentAvatarPath);
        }

        private void DrawPrefabList()
        {
            GUILayout.Label(_prefabListMode == PrefabListMode.Referenced ? T("prefabListTitle") : T("unusedPrefabListTitle"), EditorStyles.boldLabel);
            if (_currentCache != null && _currentCache.partUsages != null && _currentCache.partUsages.Count > 0)
            {
                var preview = string.Join("\n", _currentCache.partUsages.Take(3).Select(x => $"{x.status} | {x.confidence}% | {x.reason}"));
                EditorGUILayout.HelpBox("Detect Modified / Unpacked Parts\n" + preview, MessageType.None);
            }

            if (_currentCache == null)
            {
                EditorGUILayout.HelpBox(T("needAvatarCache"), MessageType.Info);
                if (GUILayout.Button(T("goAvatarSelect"))) _activeTab = TopTab.AvatarSelect;
                return;
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(string.Format(T("currentAvatarLine"), _currentCache.avatarDisplayName, _currentCache.prefabs.Count), EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(T("referencedMode"), EditorStyles.miniButtonLeft, GUILayout.Width(130)))
            {
                _prefabListMode = PrefabListMode.Referenced;
            }
            if (GUILayout.Button(T("unusedMode"), EditorStyles.miniButtonRight, GUILayout.Width(130)))
            {
                LoadCachedUnusedPrefabsForCurrentAvatar();
                _prefabListMode = PrefabListMode.Unused;
            }
            EditorGUILayout.EndHorizontal();

            if (_prefabListMode == PrefabListMode.Unused && (_unusedPrefabEntries == null || _unusedPrefabEntries.Count == 0))
            {
                LoadCachedUnusedPrefabsForCurrentAvatar();
            }
            var entries = _prefabListMode == PrefabListMode.Referenced ? _currentCache.prefabs : _unusedPrefabEntries;
            var filtered = SortPrefabEntries(entries.Where(x => x != null && IsProjectPath(x.prefabPath) && MatchesSearch(x.prefabPath))).ToList();
            _currentVisiblePaths = filtered.Select(x => x.prefabPath).ToList();

            if (filtered.Count == 0)
            {
                EditorGUILayout.HelpBox(_prefabListMode == PrefabListMode.Referenced ? T("noPrefabsFound") : T("noUnusedPrefabsFound"), MessageType.Info);
                return;
            }

            if (_listView)
            {
                foreach (var entry in filtered)
                {
                    DrawPrefabListRow(entry);
                }
            }
            else
            {
                DrawPrefabGrid(filtered);
            }
        }

        private void DrawPrefabGrid(List<PrefabPointerEntry> entries)
        {
            var usableWidth = Mathf.Max(360f, position.width - 250f - 330f - 40f);
            var cardWidth = 168f;
            var columns = Mathf.Max(1, Mathf.FloorToInt(usableWidth / cardWidth));
            var index = 0;

            while (index < entries.Count)
            {
                EditorGUILayout.BeginHorizontal();
                for (var c = 0; c < columns && index < entries.Count; c++, index++)
                {
                    DrawPrefabCard(entries[index], cardWidth - 8f);
                }
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawPrefabCard(PrefabPointerEntry entry, float width)
        {
            var rect = GUILayoutUtility.GetRect(width, 178, GUILayout.Width(width));
            var selected = IsPathSelected(entry.prefabPath);
            if (selected) EditorGUI.DrawRect(rect, new Color(0.22f, 0.38f, 0.62f, 0.5f));
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);

            var previewRect = new Rect(rect.x + 8, rect.y + 8, rect.width - 16, 96);
            var preview = GetBestPreview(entry.prefabPath, true);
            if (preview != null) GUI.DrawTexture(previewRect, preview, ScaleMode.ScaleToFit);
            else GUI.Label(previewRect, TypeIconForPath(entry.prefabPath), CenteredMiniLabel());

            var nameRect = new Rect(rect.x + 8, rect.y + 108, rect.width - 16, 34);
            GUI.Label(nameRect, Path.GetFileNameWithoutExtension(entry.prefabPath), EditorStyles.wordWrappedMiniLabel);
            GUI.Label(new Rect(rect.x + 8, rect.y + 143, rect.width - 16, 18), "Deps: " + entry.dependencyPaths.Count, EditorStyles.miniLabel);

            var buttonRect = new Rect(rect.x + 8, rect.y + 158, rect.width - 16, 18);
            if (GUI.Button(buttonRect, T("openAssets"), EditorStyles.miniButton))
            {
                OpenPrefabAssets(entry.prefabPath);
            }

            HandleAssetCardMouse(rect, entry.prefabPath, true);
        }

        private void DrawPrefabListRow(PrefabPointerEntry entry)
        {
            var rect = GUILayoutUtility.GetRect(1, 46, GUILayout.ExpandWidth(true));
            var selected = IsPathSelected(entry.prefabPath);
            if (selected) EditorGUI.DrawRect(rect, new Color(0.22f, 0.38f, 0.62f, 0.5f));
            else if (rect.Contains(Event.current.mousePosition)) EditorGUI.DrawRect(rect, new Color(1f, 1f, 1f, 0.06f));

            var preview = GetBestPreview(entry.prefabPath, false);
            if (preview != null) GUI.DrawTexture(new Rect(rect.x + 6, rect.y + 5, 36, 36), preview, ScaleMode.ScaleToFit);
            else GUI.Label(new Rect(rect.x + 6, rect.y + 12, 36, 20), TypeIconForPath(entry.prefabPath));

            GUI.Label(new Rect(rect.x + 50, rect.y + 5, rect.width - 190, 20), Path.GetFileNameWithoutExtension(entry.prefabPath), EditorStyles.boldLabel);
            GUI.Label(new Rect(rect.x + 50, rect.y + 25, rect.width - 190, 18), entry.prefabPath, EditorStyles.miniLabel);
            GUI.Label(new Rect(rect.xMax - 132, rect.y + 14, 70, 18), "Deps " + entry.dependencyPaths.Count, EditorStyles.miniLabel);
            if (GUI.Button(new Rect(rect.xMax - 78, rect.y + 11, 72, 22), T("openAssets"), EditorStyles.miniButton))
            {
                OpenPrefabAssets(entry.prefabPath);
            }

            HandleAssetCardMouse(rect, entry.prefabPath, true);
        }

        private void DrawAssetsTab()
        {
            GUILayout.Label(T("assetsTitle"), EditorStyles.boldLabel);
            if (string.IsNullOrEmpty(_selectedPrefabPath))
            {
                EditorGUILayout.HelpBox(T("selectPrefabFirst"), MessageType.Info);
                if (GUILayout.Button(T("goPrefabList"))) _activeTab = TopTab.PrefabList;
                return;
            }

            GUILayout.Label(string.Format(T("selectedPrefabLine"), _selectedPrefabPath), EditorStyles.wordWrappedMiniLabel);
            var entry = FindPrefabEntry(_selectedPrefabPath);
            if (entry == null)
            {
                entry = BuildPrefabEntry(_selectedPrefabPath);
            }

            var deps = (entry.dependencyPaths ?? new List<string>()).Where(IsProjectPath).Where(MatchesSearch).ToList();
            if (deps.Count == 0)
            {
                EditorGUILayout.HelpBox(T("noDeps"), MessageType.Info);
                return;
            }

            _assetScroll = EditorGUILayout.BeginScrollView(_assetScroll);
            DrawAssetCollection(deps, true);
            EditorGUILayout.EndScrollView();
        }

        private void DrawAssetCollection(List<string> paths, bool dependencyMode)
        {
            if (paths == null || paths.Count == 0)
            {
                EditorGUILayout.HelpBox(T("noAssetsToShow"), MessageType.Info);
                return;
            }

            var sortedPaths = SortAssetPaths(paths);
            _currentVisiblePaths = sortedPaths.ToList();
            if (_listView)
            {
                foreach (var path in sortedPaths)
                {
                    DrawAssetRow(path, dependencyMode);
                }
            }
            else
            {
                DrawAssetGrid(sortedPaths, dependencyMode);
            }
        }

        private void DrawAssetGrid(List<string> paths, bool dependencyMode)
        {
            var usableWidth = Mathf.Max(360f, position.width - 250f - 330f - 40f);
            var cardWidth = 150f;
            var columns = Mathf.Max(1, Mathf.FloorToInt(usableWidth / cardWidth));
            var index = 0;
            while (index < paths.Count)
            {
                EditorGUILayout.BeginHorizontal();
                for (var c = 0; c < columns && index < paths.Count; c++, index++)
                {
                    DrawAssetCard(paths[index], cardWidth - 8f, dependencyMode);
                }
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawAssetCard(string path, float width, bool dependencyMode)
        {
            var rect = GUILayoutUtility.GetRect(width, 150, GUILayout.Width(width));
            var selected = IsPathSelected(path);
            if (selected) EditorGUI.DrawRect(rect, new Color(0.22f, 0.38f, 0.62f, 0.5f));
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);

            var previewRect = new Rect(rect.x + 8, rect.y + 8, rect.width - 16, 78);
            var preview = GetBestPreview(path, true);
            if (preview != null) GUI.DrawTexture(previewRect, preview, ScaleMode.ScaleToFit);
            else GUI.Label(previewRect, TypeIconForPath(path), CenteredMiniLabel());

            GUI.Label(new Rect(rect.x + 8, rect.y + 90, rect.width - 16, 34), Path.GetFileNameWithoutExtension(path), EditorStyles.wordWrappedMiniLabel);
            GUI.Label(new Rect(rect.x + 8, rect.y + 125, rect.width - 16, 18), TypeLabelForPath(path), EditorStyles.miniLabel);

            HandleAssetCardMouse(rect, path, false);
        }

        private void DrawAssetRow(string path, bool dependencyMode)
        {
            var rect = GUILayoutUtility.GetRect(1, 42, GUILayout.ExpandWidth(true));
            var selected = IsPathSelected(path);
            if (selected) EditorGUI.DrawRect(rect, new Color(0.22f, 0.38f, 0.62f, 0.5f));
            else if (rect.Contains(Event.current.mousePosition)) EditorGUI.DrawRect(rect, new Color(1f, 1f, 1f, 0.06f));

            var preview = GetBestPreview(path, false);
            if (preview != null) GUI.DrawTexture(new Rect(rect.x + 6, rect.y + 4, 34, 34), preview, ScaleMode.ScaleToFit);
            else GUI.Label(new Rect(rect.x + 6, rect.y + 11, 34, 20), TypeIconForPath(path));

            GUI.Label(new Rect(rect.x + 48, rect.y + 4, rect.width - 160, 19), Path.GetFileNameWithoutExtension(path), EditorStyles.boldLabel);
            GUI.Label(new Rect(rect.x + 48, rect.y + 23, rect.width - 160, 16), path, EditorStyles.miniLabel);
            GUI.Label(new Rect(rect.xMax - 95, rect.y + 12, 86, 18), TypeLabelForPath(path), EditorStyles.miniLabel);

            HandleAssetCardMouse(rect, path, IsPrefab(path));
        }

        private void HandleAssetCardMouse(Rect rect, string path, bool prefabLike)
        {
            var e = Event.current;
            if (!rect.Contains(e.mousePosition)) return;

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                _dragSourcePath = path;
                _dragStartMousePosition = e.mousePosition;
                HandleSelectionClick(path, _currentVisiblePaths);
                if (prefabLike)
                {
                    SelectPrefabOnly(path);
                }
                else
                {
                    SelectAsset(path);
                }
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && e.button == 0 && _dragSourcePath == path)
            {
                if ((e.mousePosition - _dragStartMousePosition).sqrMagnitude > 16f)
                {
                    StartProjectDrag(path);
                    e.Use();
                }
            }
            else if (e.type == EventType.ContextClick)
            {
                if (!IsPathSelected(path))
                {
                    ClearMultiSelection();
                    _selectedPaths.Add(path);
                    _lastClickedPath = path;
                    if (prefabLike) SelectPrefabOnly(path);
                    else SelectAsset(path);
                }
                ShowAssetContextMenu(path);
                e.Use();
            }
        }

        private void DrawSettings()
        {
            GUILayout.Label(T("settingsTitle"), EditorStyles.boldLabel);
            GUILayout.Label(T("settingsSubtitle"), EditorStyles.wordWrappedMiniLabel);
            GUILayout.Space(8);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label(T("settingsMovedLibraryRoot"), EditorStyles.wordWrappedMiniLabel);
            if (GUILayout.Button(T("tabDashboard"), GUILayout.Height(30)))
            {
                _activeTab = TopTab.Dashboard;
            }
            if (GUILayout.Button(T("pingDataAsset"), GUILayout.Width(130)))
            {
                EditorGUIUtility.PingObject(_db);
            }
            GUILayout.Label(T("settingsNote"), EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndVertical();
        }

        private void DrawRightDetails(float width)
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(width));
            _rightScroll = EditorGUILayout.BeginScrollView(_rightScroll);

            GUILayout.Label(T("details"), EditorStyles.boldLabel);
            var selectedPaths = _selectedPaths.Where(IsProjectPath).Distinct().ToList();
            if (selectedPaths.Count > 1)
            {
                DrawBatchDetails(selectedPaths);
                EditorGUILayout.EndScrollView();
                EditorGUILayout.EndVertical();
                return;
            }

            var path = !string.IsNullOrEmpty(_selectedAssetPath) ? _selectedAssetPath : _selectedPrefabPath;
            if (string.IsNullOrEmpty(path))
            {
                EditorGUILayout.HelpBox(T("noSelectedAsset"), MessageType.Info);
                EditorGUILayout.EndScrollView();
                EditorGUILayout.EndVertical();
                return;
            }

            DrawAssetDetails(path);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawBatchDetails(List<string> paths)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label(string.Format(T("multiSelected"), paths.Count), EditorStyles.boldLabel);
            var prefabCount = paths.Count(IsPrefab);
            GUILayout.Label(string.Format(T("multiSelectedSummary"), prefabCount, paths.Count - prefabCount), EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = !string.IsNullOrEmpty(_selectedCategoryId);
            if (GUILayout.Button(T("addToCurrentCategory")))
            {
                _db.AddAssetsToCategory(_selectedCategoryId, paths);
                _db.Save();
                SetStatus(string.Format(T("addedToCategory"), paths.Count, GetSelectedCategoryName()));
            }
            GUI.enabled = prefabCount > 0;
            if (GUILayout.Button(T("instantiateToScene")))
            {
                InstantiatePrefabsInScene(paths);
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button(T("clearSelection")))
            {
                _selectedPaths.Clear();
                Repaint();
            }
            EditorGUILayout.EndVertical();

            GUILayout.Space(6);
            foreach (var path in paths.Take(30))
            {
                EditorGUILayout.BeginHorizontal();
                var preview = GetBestPreview(path, false);
                if (preview != null) GUILayout.Label(preview, GUILayout.Width(22), GUILayout.Height(22));
                else GUILayout.Label(TypeIconForPath(path), GUILayout.Width(22));
                GUILayout.Label(Path.GetFileNameWithoutExtension(path), EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();
            }
            if (paths.Count > 30) GUILayout.Label("...", EditorStyles.miniLabel);
        }

        private void DrawAssetDetails(string path)
        {
            var preview = GetBestPreview(path, true);
            if (preview != null)
            {
                var rect = GUILayoutUtility.GetRect(1, 160, GUILayout.ExpandWidth(true));
                GUI.DrawTexture(rect, preview, ScaleMode.ScaleToFit);
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label(Path.GetFileNameWithoutExtension(path), EditorStyles.boldLabel);
            GUILayout.Label(TypeLabelForPath(path), EditorStyles.miniLabel);
            GUILayout.Label(path, EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(T("ping"))) Ping(path);
            if (GUILayout.Button(T("reveal"))) Reveal(path);
            if (GUILayout.Button(T("copyPath")))
            {
                EditorGUIUtility.systemCopyBuffer = path;
                SetStatus(T("pathCopied"));
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            DrawCategoryActions(path);
            DrawTags(path);
            DrawLocalCover(path);
            DrawBoothInfoIfSupported(path);
        }

        private void DrawCategoryActions(string path)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label(T("categoryActions"), EditorStyles.boldLabel);
            GUILayout.Label(T("selectedCategory") + ": " + GetSelectedCategoryName(), EditorStyles.miniLabel);
            EditorGUILayout.BeginHorizontal();
            GUI.enabled = !string.IsNullOrEmpty(_selectedCategoryId);
            if (GUILayout.Button(T("addToCurrentCategory")))
            {
                _db.AddAssetToCategory(_selectedCategoryId, path);
                _db.Save();
                SetStatus(T("addedOneAsset"));
            }
            if (GUILayout.Button(T("removeFromCurrentCategory")))
            {
                _db.RemoveAssetFromCategory(_selectedCategoryId, path);
                _db.Save();
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawTags(string path)
        {
            var meta = _db.GetMetadata(path, true);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label(T("tags"), EditorStyles.boldLabel);
            var tagString = string.Join(", ", meta.tags ?? new List<string>());
            EditorGUI.BeginChangeCheck();
            tagString = EditorGUILayout.DelayedTextField(T("tagsHint"), tagString);
            if (EditorGUI.EndChangeCheck())
            {
                meta.tags = tagString.Split(new[] { ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
                _db.Save();
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawLocalCover(string path)
        {
            var meta = _db.GetMetadata(path, true);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label(T("localCover"), EditorStyles.boldLabel);
            var current = string.IsNullOrEmpty(meta.localCoverAssetPath) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(meta.localCoverAssetPath);
            EditorGUI.BeginChangeCheck();
            var picked = EditorGUILayout.ObjectField(T("coverAsset"), current, typeof(Texture2D), false) as Texture2D;
            if (EditorGUI.EndChangeCheck())
            {
                meta.localCoverAssetPath = picked == null ? string.Empty : AssetDatabase.GetAssetPath(picked);
                ClearPreviewCache(path);
                _db.Save();
            }
            if (!string.IsNullOrEmpty(meta.localCoverAssetPath) && GUILayout.Button(T("clearCover")))
            {
                meta.localCoverAssetPath = string.Empty;
                ClearPreviewCache(path);
                _db.Save();
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawBoothInfoIfSupported(string path)
        {
            if (!SupportsBoothInfo(path))
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Label(T("boothInfo"), EditorStyles.boldLabel);
                GUILayout.Label(T("boothHidden"), EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.EndVertical();
                return;
            }

            var meta = _db.GetMetadata(path, true);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label(T("boothInfo"), EditorStyles.boldLabel);
            var oldBoothUrl = meta.boothUrl ?? string.Empty;
            var oldBoothId = meta.boothId ?? string.Empty;
            var oldThumb = meta.boothThumbnailUrl ?? string.Empty;
            EditorGUI.BeginChangeCheck();
            meta.boothUrl = EditorGUILayout.DelayedTextField(T("boothUrl"), meta.boothUrl ?? string.Empty);
            meta.boothId = EditorGUILayout.DelayedTextField(T("boothId"), string.IsNullOrEmpty(meta.boothId) ? ExtractBoothId(meta.boothUrl) : meta.boothId);
            meta.boothTitle = EditorGUILayout.DelayedTextField(T("boothTitle"), meta.boothTitle ?? string.Empty);
            meta.boothAuthor = EditorGUILayout.DelayedTextField(T("boothAuthor"), meta.boothAuthor ?? string.Empty);
            meta.boothPrice = EditorGUILayout.DelayedTextField(T("boothPrice"), meta.boothPrice ?? string.Empty);
            meta.boothThumbnailUrl = EditorGUILayout.DelayedTextField(T("boothThumbnailUrl"), meta.boothThumbnailUrl ?? string.Empty);
            meta.notes = EditorGUILayout.DelayedTextField(T("notes"), meta.notes ?? string.Empty);
            if (EditorGUI.EndChangeCheck())
            {
                if (string.IsNullOrEmpty(meta.boothId)) meta.boothId = ExtractBoothId(meta.boothUrl);
                ClearPreviewCache(path);
                _db.Save();
                if (!_isFetchingBooth && ((meta.boothUrl ?? string.Empty) != oldBoothUrl || (meta.boothId ?? string.Empty) != oldBoothId) && (!string.IsNullOrEmpty(meta.boothUrl) || !string.IsNullOrEmpty(meta.boothId)))
                {
                    FetchBoothInfoForPath(path);
                }
                else if ((meta.boothThumbnailUrl ?? string.Empty) != oldThumb && !string.IsNullOrEmpty(meta.boothThumbnailUrl))
                {
                    StartRemotePreviewRequest(path, meta.boothThumbnailUrl);
                }
            }

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = !_isFetchingBooth;
            if (GUILayout.Button(_isFetchingBooth ? T("fetchingBooth") : T("autoFillBooth")))
            {
                FetchBoothInfoForPath(path);
            }
            GUI.enabled = !string.IsNullOrEmpty(meta.boothUrl);
            if (GUILayout.Button(T("openBooth")))
            {
                Application.OpenURL(meta.boothUrl);
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
            GUILayout.Label(T("boothAutoHint"), EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndVertical();
        }

        private void FetchBoothInfoForPath(string path)
        {
            if (_isFetchingBooth) return;
            var meta = _db.GetMetadata(path, true);
            var url = BuildBoothFetchUrl(meta);
            var searchMode = false;
            if (string.IsNullOrEmpty(url))
            {
                var keyword = Path.GetFileNameWithoutExtension(path);
                if (string.IsNullOrEmpty(keyword))
                {
                    SetStatus(T("needBoothUrl"));
                    return;
                }
                url = "https://booth.pm/en/search/" + Uri.EscapeDataString(keyword);
                searchMode = true;
            }

            _isFetchingBooth = true;
            SetStatus(T("fetchingBooth"));
            StartBoothWebRequest(meta, url, searchMode);
        }

        private void StartBoothWebRequest(AssetExtraMetadata meta, string url, bool searchMode)
        {
            var request = UnityWebRequest.Get(url);
            request.timeout = 15;
            var operation = request.SendWebRequest();
            EditorApplication.CallbackFunction poll = null;
            poll = () =>
            {
                if (!operation.isDone) return;
                EditorApplication.update -= poll;
                try
                {
                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        _isFetchingBooth = false;
                        SetStatus(T("boothFetchFailed") + ": " + request.error);
                    }
                    else if (searchMode)
                    {
                        var itemId = ExtractFirstBoothItemId(request.downloadHandler.text);
                        if (string.IsNullOrEmpty(itemId))
                        {
                            _isFetchingBooth = false;
                            SetStatus(T("boothFetchFailed"));
                        }
                        else
                        {
                            meta.boothId = itemId;
                            meta.boothUrl = "https://booth.pm/items/" + itemId;
                            request.Dispose();
                            StartBoothWebRequest(meta, meta.boothUrl, false);
                            return;
                        }
                    }
                    else
                    {
                        _isFetchingBooth = false;
                        ApplyBoothInfoFromHtml(meta, request.downloadHandler.text, url);
                        _db.Save();
                        SetStatus(T("boothFetchDone"));
                    }
                }
                finally
                {
                    request.Dispose();
                    Repaint();
                }
            };
            EditorApplication.update += poll;
        }

        private string ExtractFirstBoothItemId(string html)
        {
            if (string.IsNullOrEmpty(html)) return string.Empty;
            var match = Regex.Match(html, @"/items/(\d+)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : string.Empty;
        }

        private string BuildBoothFetchUrl(AssetExtraMetadata meta)
        {
            if (meta == null) return string.Empty;
            if (!string.IsNullOrEmpty(meta.boothUrl)) return meta.boothUrl.Trim();
            if (!string.IsNullOrEmpty(meta.boothId)) return "https://booth.pm/items/" + meta.boothId.Trim();
            return string.Empty;
        }

        private void ApplyBoothInfoFromHtml(AssetExtraMetadata meta, string html, string url)
        {
            if (meta == null || string.IsNullOrEmpty(html)) return;
            meta.boothUrl = string.IsNullOrEmpty(meta.boothUrl) ? url : meta.boothUrl;
            meta.boothId = string.IsNullOrEmpty(meta.boothId) ? ExtractBoothId(meta.boothUrl) : meta.boothId;

            var rawTitle = FirstNonEmpty(
                ExtractHtmlMeta(html, "og:title"),
                ExtractHtmlMeta(html, "twitter:title"),
                ExtractJsonLikeValue(html, "name"));
            var title = string.Empty;
            var author = string.Empty;
            SplitBoothTitle(rawTitle, out title, out author);

            if (!string.IsNullOrEmpty(title)) meta.boothTitle = title;
            var parsedAuthor = FirstNonEmpty(
                ExtractHtmlMeta(html, "booth:shop:name"),
                ExtractHtmlMeta(html, "article:author"),
                ExtractJsonLikeNestedValue(html, "seller", "name"),
                author);
            if (!string.IsNullOrEmpty(parsedAuthor)) meta.boothAuthor = parsedAuthor;

            var price = FirstNonEmpty(ExtractJsonLikeValue(html, "price"), ExtractHtmlMeta(html, "product:price:amount"));
            var currency = FirstNonEmpty(ExtractJsonLikeValue(html, "priceCurrency"), ExtractHtmlMeta(html, "product:price:currency"));
            if (!string.IsNullOrEmpty(price)) meta.boothPrice = string.IsNullOrEmpty(currency) ? price : price + " " + currency;

            var image = FirstNonEmpty(ExtractHtmlMeta(html, "og:image"), ExtractHtmlMeta(html, "twitter:image"), ExtractJsonLikeValue(html, "image"));
            if (!string.IsNullOrEmpty(image))
            {
                image = NormalizeUrl(image);
                meta.boothThumbnailUrl = image;
                ClearPreviewCache(meta.assetPath);
                StartRemotePreviewRequest(meta.assetPath, image);
            }
        }

        private string NormalizeUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return string.Empty;
            url = System.Net.WebUtility.HtmlDecode(url).Trim();
            if (url.StartsWith("//", StringComparison.Ordinal)) return "https:" + url;
            return url;
        }

        private string ExtractHtmlMeta(string html, string key)
        {
            if (string.IsNullOrEmpty(html) || string.IsNullOrEmpty(key)) return string.Empty;
            foreach (Match match in Regex.Matches(html, @"<meta\s+[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                var tag = match.Value;
                if (tag.IndexOf(key, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var content = Regex.Match(tag, @"\bcontent\s*=\s*(['""])(.*?)\1", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (content.Success) return System.Net.WebUtility.HtmlDecode(content.Groups[2].Value.Trim());
            }
            return string.Empty;
        }

        private string ExtractJsonLikeValue(string html, string key)
        {
            if (string.IsNullOrEmpty(html) || string.IsNullOrEmpty(key)) return string.Empty;
            var quoted = Regex.Match(html, "\\\"" + Regex.Escape(key) + "\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (quoted.Success) return System.Net.WebUtility.HtmlDecode(Regex.Unescape(quoted.Groups[1].Value.Trim()));
            var numeric = Regex.Match(html, "\\\"" + Regex.Escape(key) + "\\\"\\s*:\\s*([0-9]+(?:\\.[0-9]+)?)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (numeric.Success) return numeric.Groups[1].Value.Trim();
            return string.Empty;
        }

        private string ExtractJsonLikeNestedValue(string html, string parentKey, string childKey)
        {
            if (string.IsNullOrEmpty(html)) return string.Empty;
            var parent = Regex.Match(html, "\\\"" + Regex.Escape(parentKey) + "\\\"\\s*:\\s*\\{(.*?)\\}", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            return parent.Success ? ExtractJsonLikeValue(parent.Groups[1].Value, childKey) : string.Empty;
        }

        private string FirstNonEmpty(params string[] values)
        {
            if (values == null) return string.Empty;
            foreach (var value in values)
            {
                if (!string.IsNullOrEmpty(value)) return value.Trim();
            }
            return string.Empty;
        }

        private void SplitBoothTitle(string raw, out string title, out string author)
        {
            title = string.Empty;
            author = string.Empty;
            if (string.IsNullOrEmpty(raw)) return;
            raw = System.Net.WebUtility.HtmlDecode(raw).Trim();
            raw = Regex.Replace(raw, "\\s*-\\s*BOOTH.*$", string.Empty, RegexOptions.IgnoreCase).Trim();
            var parts = raw.Split(new[] { " | " }, StringSplitOptions.None);
            if (parts.Length >= 2)
            {
                title = parts[0].Trim();
                author = parts[1].Trim();
            }
            else
            {
                title = raw;
            }
        }

        private void AnalyzeCurrentAvatar()

        {
            if (!HasCurrentAvatarSelection())
            {
                SetStatus(T("needAvatar"));
                return;
            }

            var avatarObject = _currentAvatarObject;
            if (avatarObject == null && _currentCache == null)
            {
                SetStatus(T("needAvatar"));
                return;
            }

            try
            {
                var key = MakeAvatarKey(avatarObject);
                var displayName = GetAvatarDisplayName(avatarObject);
                var avatarPath = GetAvatarAssetOrSourcePath(avatarObject);
                var cache = _db.GetOrCreateAvatarCache(key);
                cache.avatarKey = key;
                cache.avatarDisplayName = displayName;
                cache.avatarAssetPath = avatarPath;
                cache.avatarSourceKind = string.IsNullOrEmpty(AssetDatabase.GetAssetPath(avatarObject)) ? "Scene" : "Project";
                cache.analyzedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                var fileInfo = GetFileInfo(avatarPath);
                cache.avatarFileSize = fileInfo.size;
                cache.avatarModifiedUtcTicks = fileInfo.modifiedTicks;

                var prefabPaths = CollectAvatarPrefabPaths(avatarObject, avatarPath);
                cache.prefabs = prefabPaths.Select(BuildPrefabEntry).Where(x => x != null).OrderBy(x => x.displayName).ToList();
                cache.partUsages = CollectAvatarPartUsages(avatarObject, cache.prefabs);
                SyncAvatarPrefabCategory(cache);

                _currentAvatarKey = cache.avatarKey;
                _currentAvatarDisplayName = cache.avatarDisplayName;
                _currentAvatarPath = cache.avatarAssetPath;
                _currentCache = cache;
                if (!string.IsNullOrEmpty(cache.autoCategoryId)) _selectedCategoryId = cache.autoCategoryId;
                _selectedPrefabPath = cache.prefabs.Count > 0 ? cache.prefabs[0].prefabPath : string.Empty;
                _selectedAssetPath = string.Empty;
                _selectedPaths.Clear();
                if (!string.IsNullOrEmpty(_selectedPrefabPath)) _selectedPaths.Add(_selectedPrefabPath);
                _lastClickedPath = _selectedPrefabPath;
                _prefabListMode = PrefabListMode.Referenced;
                _activeTab = TopTab.PrefabList;

                _db.Save();
                SaveEditorPrefs();
                SetStatus(string.Format(T("analysisDone"), cache.prefabs.Count) + " " + string.Format(T("partDetectDone"), cache.partUsages.Count));
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                SetStatus(T("analysisFailed") + ": " + ex.Message);
            }
        }

        private List<AvatarPartUsageEntry> CollectAvatarPartUsages(GameObject avatarRoot, List<PrefabPointerEntry> prefabEntries)
        {
            var result = new List<AvatarPartUsageEntry>();
            if (avatarRoot == null) return result;
            var dependencyMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in prefabEntries ?? new List<PrefabPointerEntry>())
            {
                if (entry == null || entry.dependencyPaths == null) continue;
                foreach (var dep in entry.dependencyPaths.Where(IsProjectPath))
                {
                    if (!dependencyMap.TryGetValue(dep, out var list)) dependencyMap[dep] = list = new List<string>();
                    if (!list.Contains(entry.prefabPath)) list.Add(entry.prefabPath);
                }
            }

            foreach (var renderer in avatarRoot.GetComponentsInChildren<Renderer>(true))
            {
                var go = renderer.gameObject;
                var entry = new AvatarPartUsageEntry { objectPath = GetHierarchyPath(go.transform) };
                var prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
                if (renderer is SkinnedMeshRenderer smr && smr.sharedMesh != null) entry.meshPath = AssetDatabase.GetAssetPath(smr.sharedMesh);
                if (renderer is MeshRenderer)
                {
                    var mf = renderer.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null) entry.meshPath = AssetDatabase.GetAssetPath(mf.sharedMesh);
                }
                entry.materialPaths = renderer.sharedMaterials.Where(x => x != null).Select(AssetDatabase.GetAssetPath).Where(IsProjectPath).Distinct().ToList();

                if (PrefabUtility.GetPrefabInstanceStatus(go) == PrefabInstanceStatus.Connected && IsProjectPath(prefabPath))
                {
                    entry.status = "Prefab Instance";
                    entry.confidence = 100;
                    entry.reason = "Prefab connected";
                    entry.matchedPrefabPath = prefabPath;
                }
                else
                {
                    var scores = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    if (IsProjectPath(entry.meshPath) && dependencyMap.TryGetValue(entry.meshPath, out var meshCandidates))
                        foreach (var c in meshCandidates) scores[c] = 60;
                    foreach (var mat in entry.materialPaths)
                        if (dependencyMap.TryGetValue(mat, out var matCandidates))
                            foreach (var c in matCandidates) scores[c] = (scores.TryGetValue(c, out var s) ? s : 0) + 20;
                    var best = scores.OrderByDescending(x => x.Value).FirstOrDefault();
                    if (!string.IsNullOrEmpty(best.Key) && best.Value >= 40)
                    {
                        entry.status = "Modified / Unpacked Match";
                        entry.confidence = Mathf.Clamp(best.Value, 40, 95);
                        entry.reason = "Mesh/Material similarity";
                        entry.matchedPrefabPath = best.Key;
                    }
                    else
                    {
                        entry.status = "Loose Assets";
                        entry.confidence = 0;
                        entry.reason = "No reliable prefab match";
                    }
                }
                result.Add(entry);
            }
            return result;
        }

        private static string GetHierarchyPath(Transform t)
        {
            var names = new List<string>();
            while (t != null) { names.Add(t.name); t = t.parent; }
            names.Reverse();
            return string.Join("/", names);
        }

        private void SyncAvatarPrefabCategory(AvatarPointerCache cache)
        {
            if (cache == null) return;
            var safeName = string.IsNullOrEmpty(cache.avatarDisplayName) ? "Avatar Prefabs" : cache.avatarDisplayName;
            CustomCategoryRecord category = null;
            if (!string.IsNullOrEmpty(cache.autoCategoryId))
            {
                category = _db.GetCategory(cache.autoCategoryId);
            }
            if (category == null)
            {
                category = _db.customCategories.FirstOrDefault(x => x.name == safeName);
            }
            if (category == null)
            {
                category = _db.CreateCategory(safeName);
            }

            cache.autoCategoryId = category.id;
            _db.ReplaceCategoryAssets(category.id, cache.prefabs == null
                ? new List<string>()
                : cache.prefabs.Select(x => x.prefabPath).Where(IsProjectPath));
        }

        private HashSet<string> CollectAvatarPrefabPaths(Object avatarObject, string avatarPath)
        {
            var results = new HashSet<string>();

            if (!string.IsNullOrEmpty(avatarPath) && IsPrefab(avatarPath))
            {
                foreach (var dep in AssetDatabase.GetDependencies(avatarPath, true))
                {
                    if (IsPrefab(dep) && dep != avatarPath) results.Add(dep);
                }
            }

            var go = ResolveGameObject(avatarObject);
            if (go != null)
            {
                foreach (var path in CollectPrefabInstancePathsFromHierarchy(go))
                {
                    if (IsPrefab(path) && path != avatarPath) results.Add(path);
                }

                var deps = EditorUtility.CollectDependencies(new Object[] { go });
                foreach (var depObj in deps)
                {
                    var path = AssetDatabase.GetAssetPath(depObj);
                    if (IsPrefab(path) && path != avatarPath) results.Add(path);
                }
            }

            if (results.Count == 0 && IsPrefab(avatarPath))
            {
                results.Add(avatarPath);
            }

            return results;
        }

        private IEnumerable<string> CollectPrefabInstancePathsFromHierarchy(GameObject root)
        {
            if (root == null) yield break;
            var visitedRoots = new HashSet<GameObject>();
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                var nearest = PrefabUtility.GetNearestPrefabInstanceRoot(transform.gameObject);
                if (nearest == null || visitedRoots.Contains(nearest)) continue;
                visitedRoots.Add(nearest);
                var path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(nearest);
                if (IsPrefab(path)) yield return path;
            }
        }

        private PrefabPointerEntry BuildPrefabEntry(string prefabPath)
        {
            if (!IsPrefab(prefabPath)) return null;
            var fileInfo = GetFileInfo(prefabPath);
            var deps = AssetDatabase.GetDependencies(prefabPath, true)
                .Where(IsUsefulDependency)
                .Where(x => x != prefabPath)
                .Distinct()
                .OrderBy(GetTypeSort)
                .ThenBy(Path.GetFileNameWithoutExtension)
                .ToList();

            return new PrefabPointerEntry
            {
                prefabPath = prefabPath,
                prefabGuid = AssetDatabase.AssetPathToGUID(prefabPath),
                displayName = Path.GetFileNameWithoutExtension(prefabPath),
                fileSize = fileInfo.size,
                modifiedUtcTicks = fileInfo.modifiedTicks,
                dependencyPaths = deps
            };
        }

        private void BuildUnusedPrefabsForCurrentAvatar()
        {
            if (_currentCache == null)
            {
                SetStatus(T("needAvatarCache"));
                return;
            }

            if (!AssetDatabase.IsValidFolder(_libraryRoot))
            {
                SetStatus(T("invalidLibraryRoot"));
                _unusedPrefabEntries = new List<PrefabPointerEntry>();
                _currentCache.unusedPrefabs = new List<PrefabPointerEntry>();
                _db.Save();
                return;
            }

            var used = new HashSet<string>(_currentCache.prefabs.Select(x => x.prefabPath));
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { _libraryRoot });
            _unusedPrefabEntries = guids
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(IsPrefab)
                .Where(path => !used.Contains(path))
                .Select(BuildPrefabEntry)
                .Where(x => x != null)
                .OrderBy(x => x.displayName)
                .ToList();

            _currentCache.unusedPrefabs = _unusedPrefabEntries;
            _currentCache.unusedAnalyzedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            _db.Save();
            SetStatus(string.Format(T("unusedBuilt"), _unusedPrefabEntries.Count));
        }

        private void LoadCachedUnusedPrefabsForCurrentAvatar()
        {
            if (_currentCache == null)
            {
                _unusedPrefabEntries = new List<PrefabPointerEntry>();
                return;
            }

            _unusedPrefabEntries = _currentCache.unusedPrefabs == null
                ? new List<PrefabPointerEntry>()
                : _currentCache.unusedPrefabs.Where(x => x != null).ToList();

            if (_unusedPrefabEntries.Count == 0)
            {
                SetStatus(T("unusedCacheEmpty"));
            }
        }

        private void SelectPrefabOnly(string path)
        {
            _selectedPrefabPath = path;
            _selectedAssetPath = string.Empty;
            SelectInInspector(path);
        }

        private void OpenPrefabAssets(string path)
        {
            SelectPrefabOnly(path);
            _activeTab = TopTab.Assets;
        }

        private void SelectAsset(string path)
        {
            _selectedAssetPath = path;
            if (!string.IsNullOrEmpty(path)) _selectedPrefabPath = IsPrefab(path) ? path : _selectedPrefabPath;
            SelectInInspector(path);
        }

        private bool IsPathSelected(string path)
        {
            return !string.IsNullOrEmpty(path) && _selectedPaths.Contains(path);
        }

        private void ClearMultiSelection()
        {
            _selectedPaths.Clear();
        }

        private void HandleSelectionClick(string path, List<string> visiblePaths)
        {
            if (string.IsNullOrEmpty(path)) return;
            var e = Event.current;
            var additive = e.control || (e.modifiers & EventModifiers.Command) != 0;
            var range = e.shift;

            if (range && !string.IsNullOrEmpty(_lastClickedPath) && visiblePaths != null && visiblePaths.Count > 0)
            {
                var start = visiblePaths.IndexOf(_lastClickedPath);
                var end = visiblePaths.IndexOf(path);
                if (start >= 0 && end >= 0)
                {
                    if (!additive) _selectedPaths.Clear();
                    if (start > end)
                    {
                        var temp = start;
                        start = end;
                        end = temp;
                    }
                    for (var i = start; i <= end; i++)
                    {
                        if (IsProjectPath(visiblePaths[i])) _selectedPaths.Add(visiblePaths[i]);
                    }
                    return;
                }
            }

            if (additive)
            {
                if (_selectedPaths.Contains(path)) _selectedPaths.Remove(path);
                else _selectedPaths.Add(path);
            }
            else
            {
                _selectedPaths.Clear();
                _selectedPaths.Add(path);
            }
            _lastClickedPath = path;
        }

        private List<string> GetSelectedPathsOrSingle(string fallbackPath)
        {
            var selected = _selectedPaths.Where(IsProjectPath).Distinct().ToList();
            if (selected.Count == 0 && IsProjectPath(fallbackPath)) selected.Add(fallbackPath);
            return selected;
        }

        private void StartProjectDrag(string fallbackPath)
        {
            var paths = GetSelectedPathsOrSingle(fallbackPath);
            var objects = paths
                .Select(AssetDatabase.LoadMainAssetAtPath)
                .Where(x => x != null)
                .ToArray();
            if (objects.Length == 0) return;

            DragAndDrop.PrepareStartDrag();
            DragAndDrop.objectReferences = objects;
            DragAndDrop.paths = paths.ToArray();
            DragAndDrop.StartDrag(objects.Length == 1 ? objects[0].name : string.Format(T("dragAssetsTitle"), objects.Length));
            SetStatus(string.Format(T("dragAssetsTitle"), objects.Length));
        }

        private PrefabPointerEntry FindPrefabEntry(string prefabPath)
        {
            if (string.IsNullOrEmpty(prefabPath)) return null;
            if (_currentCache != null)
            {
                var entry = _currentCache.prefabs.FirstOrDefault(x => x.prefabPath == prefabPath);
                if (entry != null) return entry;
            }
            return _unusedPrefabEntries.FirstOrDefault(x => x.prefabPath == prefabPath);
        }

        private void SetCurrentAvatar(Object obj)
        {
            _currentAvatarObject = obj;
            if (obj == null)
            {
                _currentAvatarKey = string.Empty;
                _currentAvatarDisplayName = string.Empty;
                _currentAvatarPath = string.Empty;
                _currentCache = null;
                _selectedPaths.Clear();
                _lastClickedPath = string.Empty;
                return;
            }

            _currentAvatarKey = MakeAvatarKey(obj);
            _currentAvatarDisplayName = GetAvatarDisplayName(obj);
            _currentAvatarPath = GetAvatarAssetOrSourcePath(obj);
            _currentCache = _db.FindAvatarCache(_currentAvatarKey);

            if (_currentCache != null)
            {
                if (!string.IsNullOrEmpty(_currentCache.autoCategoryId)) _selectedCategoryId = _currentCache.autoCategoryId;
                _unusedPrefabEntries = _currentCache.unusedPrefabs == null ? new List<PrefabPointerEntry>() : _currentCache.unusedPrefabs.ToList();
                _selectedPrefabPath = _currentCache.prefabs.Count > 0 ? _currentCache.prefabs[0].prefabPath : string.Empty;
                _selectedPaths.Clear();
                if (!string.IsNullOrEmpty(_selectedPrefabPath)) _selectedPaths.Add(_selectedPrefabPath);
                _lastClickedPath = _selectedPrefabPath;
                SetStatus(T("cacheLoaded"));
            }
            else
            {
                _selectedPrefabPath = string.Empty;
                _selectedPaths.Clear();
                _lastClickedPath = string.Empty;
                SetStatus(T("avatarSelected"));
            }
            _selectedAssetPath = string.Empty;
            SaveEditorPrefs();
        }

        private bool HasCurrentAvatarSelection()
        {
            return _currentAvatarObject != null || _currentCache != null;
        }

        private bool IsValidAvatarObject(Object obj)
        {
            var go = ResolveGameObject(obj);
            if (go == null) return false;

            if (HasComponentByName(go, "VRCAvatarDescriptor")) return true;
            if (HasComponentByName(go, "VRMBlendShapeProxy")) return true;
            if (HasComponentByName(go, "VRM10Object")) return true;
            if (go.GetComponentInChildren<Animator>(true) != null) return true;
            return IsPrefab(AssetDatabase.GetAssetPath(obj));
        }

        private GameObject ResolveGameObject(Object obj)
        {
            if (obj == null) return null;
            if (obj is GameObject go) return go;
            var path = AssetDatabase.GetAssetPath(obj);
            if (IsPrefab(path)) return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            return null;
        }

        private bool HasComponentByName(GameObject go, string componentName)
        {
            if (go == null) return false;
            var components = go.GetComponentsInChildren<Component>(true);
            foreach (var c in components)
            {
                if (c == null) continue;
                if (c.GetType().Name == componentName) return true;
            }
            return false;
        }

        private string MakeAvatarKey(Object obj)
        {
            if (obj == null) return _currentAvatarKey ?? string.Empty;
            var path = GetAvatarAssetOrSourcePath(obj);
            if (IsProjectPath(path))
            {
                var guid = AssetDatabase.AssetPathToGUID(path);
                if (!string.IsNullOrEmpty(guid)) return "asset:" + guid;
                return "asset-path:" + path;
            }

            var go = ResolveGameObject(obj);
            if (go != null)
            {
                var scenePath = string.IsNullOrEmpty(go.scene.path) ? "unsaved-scene" : go.scene.path;
                return "scene:" + scenePath + ":" + GetHierarchyPath(go.transform);
            }

            return "object:" + obj.GetInstanceID();
        }

        private string GetAvatarAssetOrSourcePath(Object obj)
        {
            if (obj == null) return _currentAvatarPath ?? string.Empty;
            var path = AssetDatabase.GetAssetPath(obj);
            if (IsProjectPath(path)) return path;
            var go = ResolveGameObject(obj);
            if (go == null) return string.Empty;
            var prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
            if (IsProjectPath(prefabPath)) return prefabPath;
            return string.IsNullOrEmpty(go.scene.path) ? string.Empty : go.scene.path;
        }

        private string GetAvatarDisplayName(Object obj)
        {
            if (obj == null) return _currentAvatarDisplayName ?? string.Empty;
            return obj.name;
        }

        private string GetHierarchyPath(Transform t)
        {
            if (t == null) return string.Empty;
            var names = new Stack<string>();
            while (t != null)
            {
                names.Push(t.name);
                t = t.parent;
            }
            return string.Join("/", names.ToArray());
        }

        private void AddSelectedResourceToCategory(string categoryId)
        {
            var fallback = !string.IsNullOrEmpty(_selectedAssetPath) ? _selectedAssetPath : _selectedPrefabPath;
            var paths = GetSelectedPathsOrSingle(fallback);
            if (paths.Count == 0)
            {
                SetStatus(T("noSelectedAsset"));
                return;
            }
            _db.AddAssetsToCategory(categoryId, paths);
            _db.Save();
            SetStatus(string.Format(T("addedToCategory"), paths.Count, GetSelectedCategoryName()));
        }

        private void AddSelectedPrefabTreeToCategory(string categoryId)
        {
            var selectedPrefabs = GetSelectedPathsOrSingle(_selectedPrefabPath).Where(IsPrefab).Distinct().ToList();
            if (selectedPrefabs.Count == 0)
            {
                SetStatus(T("selectPrefabFirst"));
                return;
            }

            var paths = new List<string>();
            foreach (var prefabPath in selectedPrefabs)
            {
                paths.Add(prefabPath);
                var entry = FindPrefabEntry(prefabPath) ?? BuildPrefabEntry(prefabPath);
                if (entry != null && entry.dependencyPaths != null) paths.AddRange(entry.dependencyPaths);
            }
            var distinct = paths.Where(IsProjectPath).Distinct().ToList();
            _db.AddAssetsToCategory(categoryId, distinct);
            _db.Save();
            SetStatus(string.Format(T("addedToCategory"), distinct.Count, GetSelectedCategoryName()));
        }

        private void ShowAssetContextMenu(string path)
        {
            var paths = GetSelectedPathsOrSingle(path);
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent(T("viewDetails")), false, () =>
            {
                if (IsPrefab(path)) SelectPrefabOnly(path);
                else SelectAsset(path);
                SelectInInspector(path);
            });
            if (IsPrefab(path)) menu.AddItem(new GUIContent(T("openAssets")), false, () => OpenPrefabAssets(path));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent(T("ping")), false, () => Ping(path));
            menu.AddItem(new GUIContent(T("reveal")), false, () => Reveal(path));
            if (paths.Any(IsPrefab))
            {
                menu.AddItem(new GUIContent(T("instantiateToScene")), false, () => InstantiatePrefabsInScene(paths));
            }
            else
            {
                menu.AddDisabledItem(new GUIContent(T("instantiateToScene")));
            }
            menu.AddSeparator("");
            foreach (var category in _db.customCategories)
            {
                var localCategory = category;
                menu.AddItem(new GUIContent(T("addToCategoryMenu") + "/" + localCategory.name), false, () =>
                {
                    _db.AddAssetsToCategory(localCategory.id, paths);
                    _db.Save();
                    SetStatus(string.Format(T("addedToCategory"), paths.Count, localCategory.name));
                });
            }
            menu.AddSeparator("");
            menu.AddItem(new GUIContent(T("deleteAsset")), false, () =>
            {
                if (EditorUtility.DisplayDialog(T("deleteAsset"), string.Format(T("deleteAssetsConfirm"), paths.Count), T("delete"), T("cancel")))
                {
                    foreach (var deletePath in paths)
                    {
                        AssetDatabase.DeleteAsset(deletePath);
                        ClearPreviewCache(deletePath);
                    }
                    _selectedPaths.Clear();
                    _db.Save();
                }
            });
            menu.ShowAsContext();
        }

        private void Ping(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            var obj = AssetDatabase.LoadMainAssetAtPath(path);
            if (obj != null) EditorGUIUtility.PingObject(obj);
        }

        private void SelectInInspector(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            if (IsPrefab(path))
            {
                var sceneInstance = FindSceneInstanceForPrefab(path);
                if (sceneInstance != null)
                {
                    Selection.activeGameObject = sceneInstance;
                    EditorGUIUtility.PingObject(sceneInstance);
                    return;
                }
            }

            var obj = AssetDatabase.LoadMainAssetAtPath(path);
            if (obj == null) return;
            Selection.activeObject = obj;
            EditorGUIUtility.PingObject(obj);
        }

        private GameObject FindSceneInstanceForPrefab(string prefabPath)
        {
            if (!IsPrefab(prefabPath)) return null;
            var preferredRoot = _currentAvatarObject as GameObject;
            if (preferredRoot != null && preferredRoot.scene.IsValid())
            {
                var underAvatar = preferredRoot.GetComponentsInChildren<Transform>(true)
                    .Select(x => x.gameObject)
                    .Where(go => go != null && go.scene.IsValid() && !EditorUtility.IsPersistent(go));
                foreach (var go in underAvatar)
                {
                    var instanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(go);
                    if (instanceRoot != go) continue;
                    var path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
                    if (path == prefabPath) return go;
                }
                if (_strictAvatarInstanceSelection) return null;
            }

            var all = Resources.FindObjectsOfTypeAll<GameObject>();
            foreach (var go in all)
            {
                if (go == null) continue;
                if (!go.scene.IsValid()) continue;
                if (EditorUtility.IsPersistent(go)) continue;
                var instanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(go);
                if (instanceRoot != go) continue;
                var path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
                if (path == prefabPath) return go;
            }
            return null;
        }

        private void Reveal(string path)
        {
            var absolute = ToAbsolutePath(path);
            if (File.Exists(absolute) || Directory.Exists(absolute)) EditorUtility.RevealInFinder(absolute);
        }

        private void InstantiatePrefabsInScene(IEnumerable<string> paths)
        {
            var prefabPaths = paths == null ? new List<string>() : paths.Where(IsPrefab).Distinct().ToList();
            if (prefabPaths.Count == 0)
            {
                SetStatus(T("noPrefabToInstantiate"));
                return;
            }

            var parent = Selection.activeTransform;
            GameObject lastInstance = null;
            foreach (var prefabPath in prefabPaths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null) continue;
                var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
                if (instance == null)
                {
                    instance = Instantiate(prefab);
                    instance.name = prefab.name;
                }

                if (parent != null && parent.gameObject.scene.IsValid())
                {
                    instance.transform.SetParent(parent, false);
                }
                Undo.RegisterCreatedObjectUndo(instance, T("instantiateToScene"));
                lastInstance = instance;
                EditorSceneManager.MarkSceneDirty(instance.scene);
            }

            if (lastInstance != null)
            {
                Selection.activeGameObject = lastInstance;
                SetStatus(string.Format(T("instantiatedToScene"), prefabPaths.Count));
            }
            else
            {
                SetStatus(T("noPrefabToInstantiate"));
            }
        }

        private (long size, long modifiedTicks) GetFileInfo(string assetPath)
        {
            try
            {
                var absolute = ToAbsolutePath(assetPath);
                if (File.Exists(absolute))
                {
                    var info = new FileInfo(absolute);
                    return (info.Length, info.LastWriteTimeUtc.Ticks);
                }
                if (Directory.Exists(absolute))
                {
                    var info = new DirectoryInfo(absolute);
                    return (0L, info.LastWriteTimeUtc.Ticks);
                }
            }
            catch
            {
                // ignored
            }
            return (0L, 0L);
        }

        private string ToAbsolutePath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return string.Empty;
            if (Path.IsPathRooted(assetPath)) return assetPath;
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
        }

        private bool IsProjectPath(string path)
        {
            return !string.IsNullOrEmpty(path) && path.StartsWith("Assets/", StringComparison.Ordinal);
        }

        private bool IsPrefab(string path)
        {
            return IsProjectPath(path) && string.Equals(Path.GetExtension(path), ".prefab", StringComparison.OrdinalIgnoreCase);
        }

        private bool SupportsBoothInfo(string path)
        {
            return IsPrefab(path) || AssetDatabase.IsValidFolder(path);
        }

        private bool IsUsefulDependency(string path)
        {
            if (!IsProjectPath(path)) return false;
            if (AssetDatabase.IsValidFolder(path)) return false;
            var ext = Path.GetExtension(path).ToLowerInvariant();
            switch (ext)
            {
                case ".prefab":
                case ".fbx":
                case ".obj":
                case ".blend":
                case ".mat":
                case ".png":
                case ".jpg":
                case ".jpeg":
                case ".tga":
                case ".psd":
                case ".exr":
                case ".anim":
                case ".controller":
                case ".overridecontroller":
                case ".shader":
                case ".shadergraph":
                case ".wav":
                case ".mp3":
                case ".ogg":
                case ".asset":
                    return true;
                default:
                    return false;
            }
        }

        private List<PrefabPointerEntry> SortPrefabEntries(IEnumerable<PrefabPointerEntry> entries)
        {
            var list = entries == null ? new List<PrefabPointerEntry>() : entries.Where(x => x != null).ToList();
            IOrderedEnumerable<PrefabPointerEntry> ordered;
            switch (_sortMode)
            {
                case SortMode.Size:
                    ordered = _sortDescending
                        ? list.OrderByDescending(x => GetKnownOrCurrentFileSize(x.prefabPath, x.fileSize)).ThenBy(x => x.displayName)
                        : list.OrderBy(x => GetKnownOrCurrentFileSize(x.prefabPath, x.fileSize)).ThenBy(x => x.displayName);
                    break;
                case SortMode.Type:
                    ordered = _sortDescending
                        ? list.OrderByDescending(x => TypeLabelForPath(x.prefabPath)).ThenBy(x => x.displayName)
                        : list.OrderBy(x => TypeLabelForPath(x.prefabPath)).ThenBy(x => x.displayName);
                    break;
                case SortMode.Date:
                    ordered = _sortDescending
                        ? list.OrderByDescending(x => GetKnownOrCurrentModifiedTicks(x.prefabPath, x.modifiedUtcTicks)).ThenBy(x => x.displayName)
                        : list.OrderBy(x => GetKnownOrCurrentModifiedTicks(x.prefabPath, x.modifiedUtcTicks)).ThenBy(x => x.displayName);
                    break;
                default:
                    ordered = _sortDescending
                        ? list.OrderByDescending(x => x.displayName ?? Path.GetFileNameWithoutExtension(x.prefabPath))
                        : list.OrderBy(x => x.displayName ?? Path.GetFileNameWithoutExtension(x.prefabPath));
                    break;
            }
            return ordered.ToList();
        }

        private List<string> SortAssetPaths(IEnumerable<string> paths)
        {
            var list = paths == null ? new List<string>() : paths.Where(IsProjectPath).Distinct().ToList();
            IOrderedEnumerable<string> ordered;
            switch (_sortMode)
            {
                case SortMode.Size:
                    ordered = _sortDescending
                        ? list.OrderByDescending(GetAssetSize).ThenBy(Path.GetFileNameWithoutExtension)
                        : list.OrderBy(GetAssetSize).ThenBy(Path.GetFileNameWithoutExtension);
                    break;
                case SortMode.Type:
                    ordered = _sortDescending
                        ? list.OrderByDescending(GetTypeSort).ThenBy(Path.GetFileNameWithoutExtension)
                        : list.OrderBy(GetTypeSort).ThenBy(Path.GetFileNameWithoutExtension);
                    break;
                case SortMode.Date:
                    ordered = _sortDescending
                        ? list.OrderByDescending(GetAssetModifiedTicks).ThenBy(Path.GetFileNameWithoutExtension)
                        : list.OrderBy(GetAssetModifiedTicks).ThenBy(Path.GetFileNameWithoutExtension);
                    break;
                default:
                    ordered = _sortDescending
                        ? list.OrderByDescending(Path.GetFileNameWithoutExtension)
                        : list.OrderBy(Path.GetFileNameWithoutExtension);
                    break;
            }
            return ordered.ToList();
        }

        private long GetKnownOrCurrentFileSize(string path, long knownSize)
        {
            return knownSize > 0 ? knownSize : GetAssetSize(path);
        }

        private long GetKnownOrCurrentModifiedTicks(string path, long knownTicks)
        {
            return knownTicks > 0 ? knownTicks : GetAssetModifiedTicks(path);
        }

        private long GetAssetSize(string path)
        {
            return GetFileInfo(path).size;
        }

        private long GetAssetModifiedTicks(string path)
        {
            return GetFileInfo(path).modifiedTicks;
        }

        private int GetTypeSort(string path)
        {
            if (IsPrefab(path)) return 0;
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".mat") return 1;
            if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".tga" || ext == ".psd" || ext == ".exr") return 2;
            if (ext == ".anim" || ext == ".controller" || ext == ".overridecontroller") return 3;
            if (ext == ".fbx" || ext == ".obj" || ext == ".blend") return 4;
            if (ext == ".shader" || ext == ".shadergraph") return 5;
            if (ext == ".wav" || ext == ".mp3" || ext == ".ogg") return 6;
            return 9;
        }

        private bool MatchesSearch(string path)
        {
            if (string.IsNullOrEmpty(_search)) return true;
            var q = _search.Trim();
            if (string.IsNullOrEmpty(q)) return true;
            if (!string.IsNullOrEmpty(path) && path.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            var meta = _db.GetMetadata(path, false);
            if (meta == null) return false;
            var fields = new[] { meta.boothId, meta.boothTitle, meta.boothAuthor, meta.boothUrl, meta.notes };
            if (fields.Any(x => !string.IsNullOrEmpty(x) && x.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)) return true;
            if (meta.tags != null && meta.tags.Any(x => !string.IsNullOrEmpty(x) && x.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)) return true;
            return false;
        }

        private void ClearPreviewCache(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            _previewCache.Remove(path);
            _previewCache.Remove("small:" + path);
            _previewCache.Remove("large:" + path);
        }

        private Texture2D GetBestPreview(string path)
        {
            return GetBestPreview(path, false);
        }

        private Texture2D GetBestPreview(string path, bool largePreview)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var cacheKey = (largePreview ? "large:" : "small:") + path;
            if (_previewCache.TryGetValue(cacheKey, out var cached) && cached != null) return cached;

            var meta = _db.GetMetadata(path, false);
            if (meta != null && !string.IsNullOrEmpty(meta.localCoverAssetPath))
            {
                var cover = AssetDatabase.LoadAssetAtPath<Texture2D>(meta.localCoverAssetPath);
                if (cover != null)
                {
                    _previewCache[cacheKey] = cover;
                    return cover;
                }
            }

            if (SupportsBoothInfo(path) && meta != null && !string.IsNullOrEmpty(meta.boothThumbnailUrl))
            {
                var remote = GetRemotePreview(path, meta.boothThumbnailUrl);
                if (remote != null)
                {
                    _previewCache[cacheKey] = remote;
                    return remote;
                }
            }

            if (IsImage(path))
            {
                var image = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (image != null)
                {
                    _previewCache[cacheKey] = image;
                    return image;
                }
            }

            Texture2D preview = null;
            var obj = AssetDatabase.LoadMainAssetAtPath(path);
            if (obj != null)
            {
                preview = AssetPreview.GetAssetPreview(obj);
                if (preview != null)
                {
                    _previewCache[cacheKey] = preview;
                    return preview;
                }

                if (AssetPreview.IsLoadingAssetPreview(obj.GetInstanceID()) || AssetPreview.IsLoadingAssetPreviews())
                {
                    Repaint();
                }

                if (largePreview && (IsPrefab(path) || IsModel(path)))
                {
                    preview = TryRenderStaticPreview(path, obj, 128);
                    if (preview != null)
                    {
                        _previewCache[cacheKey] = preview;
                        return preview;
                    }
                }

                if (!largePreview)
                {
                    // Small rows may use Unity's mini thumbnail temporarily, but it is intentionally not cached.
                    // This prevents a generic blue Prefab icon from blocking the later 3D AssetPreview.
                    preview = AssetPreview.GetMiniThumbnail(obj);
                    if (preview != null) return preview;
                }
            }

            if (SupportsBoothInfo(path))
            {
                preview = FindSiblingImagePreview(path);
                if (preview != null)
                {
                    _previewCache[cacheKey] = preview;
                    return preview;
                }
            }

            return null;
        }

        private Texture2D GetRemotePreview(string path, string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            if (_remotePreviewCache.TryGetValue(url, out var cached) && cached != null) return cached;
            StartRemotePreviewRequest(path, url);
            return null;
        }

        private void StartRemotePreviewRequest(string path, string url)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(url)) return;
            if (_remotePreviewCache.ContainsKey(url) || _remotePreviewRequests.Contains(url)) return;

            _remotePreviewRequests.Add(url);
            var request = UnityWebRequestTexture.GetTexture(url);
            request.timeout = 15;
            var operation = request.SendWebRequest();
            EditorApplication.CallbackFunction poll = null;
            poll = () =>
            {
                if (!operation.isDone) return;
                EditorApplication.update -= poll;
                try
                {
                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        var tex = DownloadHandlerTexture.GetContent(request);
                        if (tex != null)
                        {
                            _remotePreviewCache[url] = tex;
                            ClearPreviewCache(path);
                        }
                    }
                }
                finally
                {
                    _remotePreviewRequests.Remove(url);
                    request.Dispose();
                    Repaint();
                }
            };
            EditorApplication.update += poll;
        }

        private Texture2D TryRenderStaticPreview(string path, Object obj, int size)
        {
            Editor editor = null;
            try
            {
                editor = Editor.CreateEditor(obj);
                if (editor == null) return null;
                var subAssets = AssetDatabase.LoadAllAssetsAtPath(path);
                return editor.RenderStaticPreview(path, subAssets, size, size);
            }
            catch
            {
                return null;
            }
            finally
            {
                if (editor != null) DestroyImmediate(editor);
            }
        }

        private bool IsModel(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".fbx" || ext == ".obj" || ext == ".blend";
        }

        private Texture2D FindSiblingImagePreview(string path)
        {
            try
            {
                var directory = AssetDatabase.IsValidFolder(path) ? path : Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(directory)) return null;
                var abs = ToAbsolutePath(directory);
                if (!Directory.Exists(abs)) return null;
                var image = Directory.GetFiles(abs, "*.*", SearchOption.TopDirectoryOnly)
                    .Select(x => x.Replace("\\", "/"))
                    .Select(x => "Assets" + x.Substring(Application.dataPath.Replace("\\", "/").Length))
                    .FirstOrDefault(IsImage);
                return string.IsNullOrEmpty(image) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(image);
            }
            catch
            {
                return null;
            }
        }

        private bool IsImage(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".tga" || ext == ".psd" || ext == ".exr";
        }

        private Texture GetMiniIconForPath(string path)
        {
            var tex = GetBestPreview(path);
            if (tex != null) return tex;
            return EditorGUIUtility.IconContent("Prefab Icon").image;
        }

        private string TypeIconForPath(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return "📁";
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".prefab") return "🧩";
            if (IsImage(path)) return "🖼";
            if (ext == ".mat") return "◈";
            if (ext == ".anim" || ext == ".controller" || ext == ".overridecontroller") return "▶";
            if (ext == ".fbx" || ext == ".obj" || ext == ".blend") return "◆";
            if (ext == ".shader" || ext == ".shadergraph") return "S";
            if (ext == ".wav" || ext == ".mp3" || ext == ".ogg") return "♪";
            return "•";
        }

        private string TypeLabelForPath(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return "Folder";
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".prefab") return "Prefab";
            if (IsImage(path)) return "Texture";
            if (ext == ".mat") return "Material";
            if (ext == ".anim") return "Animation";
            if (ext == ".controller" || ext == ".overridecontroller") return "Animator";
            if (ext == ".fbx" || ext == ".obj" || ext == ".blend") return "Model";
            if (ext == ".shader" || ext == ".shadergraph") return "Shader";
            if (ext == ".wav" || ext == ".mp3" || ext == ".ogg") return "Audio";
            return "Other";
        }

        private GUIStyle CenteredMiniLabel()
        {
            var style = new GUIStyle(EditorStyles.boldLabel);
            style.alignment = TextAnchor.MiddleCenter;
            style.fontSize = 24;
            return style;
        }

        private string ExtractBoothId(string url)
        {
            if (string.IsNullOrEmpty(url)) return string.Empty;
            var marker = "/items/";
            var index = url.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0) return string.Empty;
            var start = index + marker.Length;
            var end = url.IndexOfAny(new[] { '/', '?', '#', '&' }, start);
            if (end < 0) end = url.Length;
            return url.Substring(start, end - start);
        }

        private string GetSelectedCategoryName()
        {
            var category = _db.GetCategory(_selectedCategoryId);
            return category == null ? T("none") : category.name;
        }

        private void SetStatus(string msg)
        {
            _statusMessage = msg;
            _statusUntil = EditorApplication.timeSinceStartup + 3.5f;
            Repaint();
        }

        private string T(string key)
        {
            switch (_language)
            {
                case Language.Japanese:
                    return TJ(key);
                case Language.English:
                    return TE(key);
                default:
                    return TC(key);
            }
        }

        private string TC(string key)
        {
            switch (key)
            {
                case "search": return "搜索";
                case "clear": return "清空";
                case "language": return "语言";
                case "view": return "视图";
                case "sort": return "排序";
                case "sortName": return "名称";
                case "sortSize": return "大小";
                case "sortType": return "类型";
                case "sortDate": return "日期";
                case "listView": return "列表";
                case "gridView": return "网格";
                case "tabDashboard": return "首页";
                case "tabLibrary": return "资产库";
                case "tabAvatarSelect": return "Avatar Select";
                case "tabPrefabList": return "当前Avatar Prefab List";
                case "tabAssets": return "Assets";
                case "tabSettings": return "设置";
                case "workflow": return "使用流程";
                case "workflowLines": return "先选 Avatar → 点检索 → 看它用到的 Prefab → 点查看资产。";
                case "customCategories": return "自定义分类";
                case "newCategory": return "新分类";
                case "createCategory": return "创建分类";
                case "rename": return "重命名";
                case "deleteCategory": return "删除分类";
                case "deleteCategoryConfirm": return "确定删除这个分类？不会删除真实资产。";
                case "delete": return "删除";
                case "cancel": return "取消";
                case "addSelectedAsset": return "加入当前选中资源";
                case "addPrefabTree": return "加入当前 Prefab 树";
                case "addedToCategory": return "已加入 {0} 个资源到 {1}";
                case "recentAvatars": return "最近分析 Avatar";
                case "noCachedAvatar": return "还没有 Avatar 指针索引。";
                case "quickFilters": return "当前状态";
                case "selectedCategory": return "当前分类";
                case "currentAvatar": return "当前 Avatar";
                case "none": return "无";
                case "dashboardTitle": return "轻量 Avatar 资产管理";
                case "dashboardSubtitle": return "打开就能用：先选 Avatar，点检索，工具只记住这个 Avatar 用到的 Prefab。";
                case "cachedAvatars": return "已缓存 Avatar";
                case "cachedPrefabs": return "已记录 Prefab";
                case "categorizedAssets": return "分类内资产";
                case "goAvatarSelect": return "选择 Avatar";
                case "goLibrary": return "打开资产库";
                case "recentAnalysis": return "最近检索";
                case "cacheSummary": return "Prefab: {0} ｜ 检索时间: {1}";
                case "open": return "打开";
                case "libraryTitle": return "资产库";
                case "librarySubtitle": return "这里看自定义分类和已记录的资产，不会偷偷全项目扫描。";
                case "avatarSelectTitle": return "1. 选择 Avatar";
                case "avatarSelectSubtitle": return "把 Avatar 拖进来，然后点检索。以后再次选择它会直接读取记录。";
                case "step1SelectAvatar": return "Avatar 输入框";
                case "avatarField": return "当前选中 Avatar";
                case "dragAvatarHere": return "把 Avatar prefab 或场景 Avatar 实例拖到这里";
                case "selectedAvatarBox": return "已选择：{0}\n点击“检索当前 Avatar Prefab”生成指针索引";
                case "searchCurrentAvatarPrefabs": return "检索当前 Avatar Prefab";
                case "openCachedPrefabList": return "打开 Prefab List";
                case "sceneOrUnknown": return "场景对象或未知路径";
                case "noCacheYet": return "还没记录。点一次检索，以后会自动记住。";
                case "unusedTitle": return "未引用 Prefab";
                case "unusedExplain": return "从资源目录里找 Prefab，排除当前 Avatar 已经用到的。";
                case "showUnusedPrefabs": return "显示未被当前 Avatar 引用的 Prefab";
                case "prefabListTitle": return "2. 当前 Avatar 引用的 Prefab";
                case "unusedPrefabListTitle": return "未被当前 Avatar 引用的 Prefab";
                case "needAvatarCache": return "请先选择 Avatar 并检索当前 Avatar Prefab。";
                case "currentAvatarLine": return "Avatar: {0} ｜ 引用 Prefab: {1}";
                case "referencedMode": return "已引用 Prefab";
                case "unusedMode": return "未引用 Prefab";
                case "noPrefabsFound": return "没有找到 Prefab 引用。可能该 Avatar 已经解包，或子对象不是 Prefab 实例。";
                case "noUnusedPrefabsFound": return "没有找到未引用 Prefab，或资源目录尚未设置。";
                case "openAssets": return "查看资产";
                case "assetsTitle": return "3. Prefab 引用资产";
                case "selectPrefabFirst": return "请先在 Prefab List 中选择一个 Prefab。";
                case "goPrefabList": return "返回 Prefab List";
                case "selectedPrefabLine": return "当前 Prefab: {0}";
                case "noDeps": return "这个 Prefab 没有可显示的依赖资产。";
                case "noAssetsToShow": return "没有资产可显示。";
                case "settingsTitle": return "设置";
                case "settingsSubtitle": return "所有设置都会持久化。这里只保存轻量路径和指针索引，不自动做全项目扫描。";
                case "libraryRoot": return "资源目录";
                case "useSelectedFolder": return "使用当前选中文件夹";
                case "pingDataAsset": return "定位数据库";
                case "settingsNote": return "未引用 Prefab 功能只会扫描这个资源目录下的 t:Prefab。";
                case "strictAvatarInstanceSelection": return "只选择该Avatar下的实例";
                case "resetDashboardSettings": return "Reset 首页设置";
                case "dashboardSettingsReset": return "插件设置和缓存已重置。";
                case "resetAllConfirm": return "确定重置所有持久化设置和缓存吗？该操作不可撤销。";
                case "resetConfirmButton": return "确认重置";
                case "details": return "资源详情";
                case "noSelectedAsset": return "当前没有选中资源。";
                case "ping": return "定位";
                case "reveal": return "打开文件夹";
                case "copyPath": return "复制路径";
                case "pathCopied": return "路径已复制。";
                case "categoryActions": return "分类操作";
                case "addToCurrentCategory": return "加入当前分类";
                case "removeFromCurrentCategory": return "从当前分类移除";
                case "addedOneAsset": return "已加入当前分类。";
                case "tags": return "标签";
                case "tagsHint": return "逗号分隔";
                case "localCover": return "本地封面映射";
                case "coverAsset": return "封面图片";
                case "clearCover": return "清除封面";
                case "boothInfo": return "Booth 信息";
                case "boothHidden": return "该资源类型不显示 Booth 信息。只有 Prefab 或文件夹/包显示 Booth 链接、价格、作者等。";
                case "boothUrl": return "Booth URL";
                case "boothId": return "Booth ID";
                case "boothTitle": return "标题";
                case "boothAuthor": return "作者";
                case "boothPrice": return "价格";
                case "notes": return "备注";
                case "boothThumbnailUrl": return "缩略图 URL";
                case "autoFillBooth": return "联网自动填写";
                case "fetchingBooth": return "正在获取 Booth 信息...";
                case "boothAutoHint": return "可先填 Booth URL/ID；不填时会用资产名尝试搜索。抓不到时可手动补。";
                case "needBoothUrl": return "请填写 Booth URL/ID，或改成更容易搜索的资产名。";
                case "boothFetchFailed": return "Booth 信息获取失败";
                case "boothFetchDone": return "Booth 信息已更新。";
                case "avatarCardHint": return "点击可选中 Avatar；右键可操作；可拖到 Hierarchy。";
                case "dashboardSettings": return "常用设置";
                case "openBooth": return "打开 Booth";
                case "invalidAvatarIgnored": return "无效 Avatar 已忽略。";
                case "avatarSelected": return "Avatar 已选择。";
                case "needAvatar": return "请先选择 Avatar。";
                case "analysisDone": return "检索完成：找到 {0} 个 Prefab。";
                case "partDetectDone": return "部件检测：{0} 项。";
                case "analysisFailed": return "检索失败";
                case "cacheLoaded": return "已加载该 Avatar 的指针索引缓存。";
                case "invalidLibraryRoot": return "资源目录无效。请到首页修改。";
                case "unusedBuilt": return "未引用 Prefab 列表已生成：{0} 个。";
                case "unusedCacheEmpty": return "还没有未引用 Prefab 记录。请回到 Avatar Select 点击“显示未被当前 Avatar 引用的 Prefab”。";
                case "categoryOrderUpdated": return "分类顺序已更新。";
                case "viewDetails": return "查看详情";
                case "addToCategoryMenu": return "加入分类";
                case "deleteAsset": return "删除资产";
                case "assetFolderSettings": return "资源目录";
                case "selectFolderFirst": return "请先在 Project 中选择一个文件夹。";
                case "libraryRootHint": return "未引用 Prefab 只扫描这个目录；不做全局索引。";
                case "recentImportDetected": return "检测到最近导入资源";
                case "recentImportSummary": return "最近导入/修改 {0} 个资源 ｜ {1}";
                case "targetCategory": return "目标分类";
                case "addImportedToCategory": return "加入分类";
                case "ignore": return "忽略";
                case "settingsMovedLibraryRoot": return "常用设置已放到首页三个按钮下方。";
                case "dragAssetsTitle": return "拖拽 {0} 个资源";
                case "multiSelected": return "已多选 {0} 个资源";
                case "multiSelectedSummary": return "Prefab: {0} ｜ 其他资源: {1}";
                case "clearSelection": return "清空选择";
                case "instantiateToScene": return "导入到场景";
                case "noPrefabToInstantiate": return "当前选择中没有可导入场景的 Prefab。";
                case "instantiatedToScene": return "已导入 {0} 个 Prefab 到场景。";
                case "deleteAssetsConfirm": return "将删除 {0} 个真实项目资源，请确认。";
                case "deleteAssetConfirm": return "这会从项目中删除真实资产，请确认。";
                default: return key;
            }
        }

        private string TJ(string key)
        {
            switch (key)
            {
                case "search": return "検索";
                case "clear": return "クリア";
                case "language": return "言語";
                case "view": return "表示";
                case "sort": return "ソート";
                case "sortName": return "名前";
                case "sortSize": return "サイズ";
                case "sortType": return "種類";
                case "sortDate": return "日付";
                case "listView": return "リスト";
                case "gridView": return "グリッド";
                case "tabDashboard": return "ホーム";
                case "tabLibrary": return "ライブラリ";
                case "tabAvatarSelect": return "Avatar Select";
                case "tabPrefabList": return "現在Avatar Prefab List";
                case "tabAssets": return "Assets";
                case "tabSettings": return "設定";
                case "workflow": return "手順";
                case "workflowLines": return "Avatarを選ぶ → 検索 → 使っているPrefabを見る → アセットを見る。";
                case "customCategories": return "カスタム分類";
                case "newCategory": return "新規分類";
                case "createCategory": return "分類を作成";
                case "rename": return "名前変更";
                case "deleteCategory": return "分類を削除";
                case "deleteCategoryConfirm": return "分類を削除しますか？実際のアセットは削除されません。";
                case "delete": return "削除";
                case "cancel": return "キャンセル";
                case "addSelectedAsset": return "選択中のアセットを追加";
                case "addPrefabTree": return "現在のPrefabツリーを追加";
                case "addedToCategory": return "{0} 個のアセットを {1} に追加しました";
                case "recentAvatars": return "最近分析したAvatar";
                case "noCachedAvatar": return "まだAvatarインデックスがありません。";
                case "quickFilters": return "現在の状態";
                case "selectedCategory": return "現在の分類";
                case "currentAvatar": return "現在のAvatar";
                case "none": return "なし";
                case "dashboardTitle": return "軽量Avatarアセット管理";
                case "dashboardSubtitle": return "まずAvatarを選び、検索を押すだけ。このAvatarが使うPrefabだけを記録します。";
                case "cachedAvatars": return "キャッシュAvatar";
                case "cachedPrefabs": return "記録済みPrefab";
                case "categorizedAssets": return "分類済みアセット";
                case "goAvatarSelect": return "Avatar選択";
                case "goLibrary": return "ライブラリを開く";
                case "recentAnalysis": return "最近の検索";
                case "cacheSummary": return "Prefab: {0} ｜ 検索時刻: {1}";
                case "open": return "開く";
                case "libraryTitle": return "ライブラリ";
                case "librarySubtitle": return "カスタム分類と記録済みアセットを表示します。全体スキャンはしません。";
                case "avatarSelectTitle": return "1. Avatarを選択";
                case "avatarSelectSubtitle": return "Avatarをここにドロップして検索。次回から記録をすぐ読み込みます。";
                case "step1SelectAvatar": return "Avatar入力欄";
                case "avatarField": return "現在選択中Avatar";
                case "dragAvatarHere": return "Avatar prefab またはシーン内Avatarをここへドロップ";
                case "selectedAvatarBox": return "選択中：{0}\n検索ボタンでPrefabインデックスを作成";
                case "searchCurrentAvatarPrefabs": return "現在AvatarのPrefabを検索";
                case "openCachedPrefabList": return "Prefab Listを開く";
                case "sceneOrUnknown": return "シーンオブジェクトまたは不明なパス";
                case "noCacheYet": return "まだ記録がありません。一度検索すると次回から使えます。";
                case "unusedTitle": return "未使用Prefab";
                case "unusedExplain": return "アセットフォルダのPrefabから、現在Avatarが使うPrefabを除外します。";
                case "showUnusedPrefabs": return "現在Avatar未使用のPrefabを表示";
                case "prefabListTitle": return "2. 現在Avatarが参照するPrefab";
                case "unusedPrefabListTitle": return "現在Avatar未使用のPrefab";
                case "needAvatarCache": return "先にAvatarを選択してPrefabを検索してください。";
                case "currentAvatarLine": return "Avatar: {0} ｜ 参照Prefab: {1}";
                case "referencedMode": return "参照Prefab";
                case "unusedMode": return "未使用Prefab";
                case "noPrefabsFound": return "Prefab参照が見つかりません。Avatarが展開済み、または子オブジェクトがPrefabではない可能性があります。";
                case "noUnusedPrefabsFound": return "未使用Prefabが見つかりません。";
                case "openAssets": return "アセットを見る";
                case "assetsTitle": return "3. Prefab参照アセット";
                case "selectPrefabFirst": return "まずPrefab ListでPrefabを選択してください。";
                case "goPrefabList": return "Prefab Listへ戻る";
                case "selectedPrefabLine": return "現在のPrefab: {0}";
                case "noDeps": return "表示できる依存アセットがありません。";
                case "noAssetsToShow": return "表示するアセットがありません。";
                case "settingsTitle": return "設定";
                case "settingsSubtitle": return "設定は保存されます。軽量パスとポインターインデックスのみ保存し、全体スキャンはしません。";
                case "libraryRoot": return "アセットフォルダ";
                case "useSelectedFolder": return "選択フォルダを使用";
                case "pingDataAsset": return "DBを表示";
                case "settingsNote": return "未使用Prefab機能はこのフォルダ内の t:Prefab のみスキャンします。";
                case "strictAvatarInstanceSelection": return "このAvatar配下のインスタンスのみ選択";
                case "resetDashboardSettings": return "ホーム設定をリセット";
                case "dashboardSettingsReset": return "プラグイン設定とキャッシュをリセットしました。";
                case "resetAllConfirm": return "保存設定とキャッシュをすべてリセットします。元に戻せません。よろしいですか？";
                case "resetConfirmButton": return "リセット実行";
                case "details": return "詳細";
                case "noSelectedAsset": return "アセットが選択されていません。";
                case "ping": return "Ping";
                case "reveal": return "フォルダを開く";
                case "copyPath": return "パスをコピー";
                case "pathCopied": return "パスをコピーしました。";
                case "categoryActions": return "分類操作";
                case "addToCurrentCategory": return "現在分類に追加";
                case "removeFromCurrentCategory": return "現在分類から削除";
                case "addedOneAsset": return "現在分類に追加しました。";
                case "tags": return "タグ";
                case "tagsHint": return "カンマ区切り";
                case "localCover": return "ローカルカバー";
                case "coverAsset": return "カバー画像";
                case "clearCover": return "カバーをクリア";
                case "boothInfo": return "Booth情報";
                case "boothHidden": return "この種類ではBooth情報を表示しません。Prefabまたはフォルダのみ表示します。";
                case "boothUrl": return "Booth URL";
                case "boothId": return "Booth ID";
                case "boothTitle": return "タイトル";
                case "boothAuthor": return "作者";
                case "boothPrice": return "価格";
                case "notes": return "メモ";
                case "boothThumbnailUrl": return "サムネイルURL";
                case "autoFillBooth": return "Booth情報を自動取得";
                case "fetchingBooth": return "Booth情報を取得中...";
                case "boothAutoHint": return "Booth URL/IDがあれば優先使用。空の場合はアセット名で検索します。";
                case "needBoothUrl": return "Booth URL/IDを入力するか、検索しやすい名前にしてください。";
                case "boothFetchFailed": return "Booth情報の取得に失敗";
                case "boothFetchDone": return "Booth情報を更新しました。";
                case "avatarCardHint": return "クリックでAvatarを選択。右クリック操作、Hierarchyへのドラッグも可能。";
                case "dashboardSettings": return "よく使う設定";
                case "openBooth": return "Boothを開く";
                case "invalidAvatarIgnored": return "無効なAvatarは無視しました。";
                case "avatarSelected": return "Avatarを選択しました。";
                case "needAvatar": return "先にAvatarを選択してください。";
                case "analysisDone": return "検索完了：{0} 個のPrefab。";
                case "partDetectDone": return "パーツ検出: {0} 件。";
                case "analysisFailed": return "検索失敗";
                case "cacheLoaded": return "このAvatarのキャッシュを読み込みました。";
                case "invalidLibraryRoot": return "アセットフォルダが無効です。ホームで変更してください。";
                case "unusedBuilt": return "未使用Prefabリスト作成：{0} 個。";
                case "viewDetails": return "詳細を見る";
                case "addToCategoryMenu": return "分類へ追加";
                case "deleteAsset": return "アセット削除";
                case "assetFolderSettings": return "アセットフォルダ";
                case "selectFolderFirst": return "Projectでフォルダを選択してください。";
                case "libraryRootHint": return "未使用Prefabはこのフォルダだけをスキャンします。全体インデックスは作成しません。";
                case "recentImportDetected": return "最近インポートしたアセットを検出";
                case "recentImportSummary": return "最近インポート/変更: {0} 個 ｜ {1}";
                case "targetCategory": return "追加先分類";
                case "addImportedToCategory": return "分類へ追加";
                case "ignore": return "無視";
                case "settingsMovedLibraryRoot": return "よく使う設定はホームの3つのボタン下へ移動しました。";
                case "dragAssetsTitle": return "{0} 個のアセットをドラッグ";
                case "multiSelected": return "{0} 個のアセットを選択中";
                case "multiSelectedSummary": return "Prefab: {0} ｜ その他: {1}";
                case "clearSelection": return "選択をクリア";
                case "instantiateToScene": return "シーンに追加";
                case "noPrefabToInstantiate": return "シーンに追加できるPrefabが選択されていません。";
                case "instantiatedToScene": return "{0} 個のPrefabをシーンに追加しました。";
                case "deleteAssetsConfirm": return "{0} 個の実アセットを削除します。よろしいですか？";
                case "deleteAssetConfirm": return "実際のアセットを削除します。よろしいですか？";
                default: return TC(key);
            }
        }

        private string TE(string key)
        {
            switch (key)
            {
                case "search": return "Search";
                case "clear": return "Clear";
                case "language": return "Language";
                case "view": return "View";
                case "sort": return "Sort";
                case "sortName": return "Name";
                case "sortSize": return "Size";
                case "sortType": return "Type";
                case "sortDate": return "Date";
                case "listView": return "List";
                case "gridView": return "Grid";
                case "tabDashboard": return "Dashboard";
                case "tabLibrary": return "Library";
                case "tabAvatarSelect": return "Avatar Select";
                case "tabPrefabList": return "Current Avatar Prefab List";
                case "tabAssets": return "Assets";
                case "tabSettings": return "Settings";
                case "workflow": return "Workflow";
                case "workflowLines": return "Pick an Avatar → Scan → see used Prefabs → View Assets when needed.";
                case "customCategories": return "Custom Categories";
                case "newCategory": return "New Category";
                case "createCategory": return "Create Category";
                case "rename": return "Rename";
                case "deleteCategory": return "Delete Category";
                case "deleteCategoryConfirm": return "Delete this category? Real assets will not be deleted.";
                case "delete": return "Delete";
                case "cancel": return "Cancel";
                case "addSelectedAsset": return "Add Selected Asset";
                case "addPrefabTree": return "Add Current Prefab Tree";
                case "addedToCategory": return "Added {0} assets to {1}";
                case "recentAvatars": return "Recent Avatars";
                case "noCachedAvatar": return "No avatar pointer cache yet.";
                case "quickFilters": return "Current State";
                case "selectedCategory": return "Selected Category";
                case "currentAvatar": return "Current Avatar";
                case "none": return "None";
                case "dashboardTitle": return "Lightweight Avatar Asset Manager";
                case "dashboardSubtitle": return "Start simple: pick an Avatar, scan once, and the tool remembers the Prefabs it uses.";
                case "cachedAvatars": return "Cached Avatars";
                case "cachedPrefabs": return "Recorded Prefabs";
                case "categorizedAssets": return "Categorized Assets";
                case "goAvatarSelect": return "Select Avatar";
                case "goLibrary": return "Open Library";
                case "recentAnalysis": return "Recent Analysis";
                case "cacheSummary": return "Prefabs: {0} | Analyzed: {1}";
                case "open": return "Open";
                case "libraryTitle": return "Library";
                case "librarySubtitle": return "Shows custom categories and remembered assets. No hidden full-project scan.";
                case "avatarSelectTitle": return "1. Select Avatar";
                case "avatarSelectSubtitle": return "Drop an Avatar here, then scan. Next time it loads from memory.";
                case "step1SelectAvatar": return "Avatar Input";
                case "avatarField": return "Current Avatar";
                case "dragAvatarHere": return "Drop Avatar prefab or scene Avatar instance here";
                case "selectedAvatarBox": return "Selected: {0}\nClick search to create the prefab pointer index";
                case "searchCurrentAvatarPrefabs": return "Search Current Avatar Prefabs";
                case "openCachedPrefabList": return "Open Prefab List";
                case "sceneOrUnknown": return "Scene object or unknown path";
                case "noCacheYet": return "Not remembered yet. Scan once and it will load faster next time.";
                case "unusedTitle": return "Unused Prefabs";
                case "unusedExplain": return "Looks in your asset folder, then hides Prefabs already used by this Avatar.";
                case "showUnusedPrefabs": return "Show Prefabs Not Used By Current Avatar";
                case "prefabListTitle": return "2. Prefabs Referenced By Current Avatar";
                case "unusedPrefabListTitle": return "Prefabs Not Used By Current Avatar";
                case "needAvatarCache": return "Select an Avatar and search current Avatar prefabs first.";
                case "currentAvatarLine": return "Avatar: {0} | Referenced Prefabs: {1}";
                case "referencedMode": return "Referenced Prefabs";
                case "unusedMode": return "Unused Prefabs";
                case "noPrefabsFound": return "No prefab references found. The Avatar may be unpacked or child objects may not be prefab instances.";
                case "noUnusedPrefabsFound": return "No unused prefabs found, or the library root is not configured.";
                case "openAssets": return "View Assets";
                case "assetsTitle": return "3. Prefab Referenced Assets";
                case "selectPrefabFirst": return "Select a prefab in the Prefab List first.";
                case "goPrefabList": return "Back to Prefab List";
                case "selectedPrefabLine": return "Current Prefab: {0}";
                case "noDeps": return "This prefab has no displayable dependencies.";
                case "noAssetsToShow": return "No assets to show.";
                case "settingsTitle": return "Settings";
                case "settingsSubtitle": return "Settings are persisted. The tool stores lightweight paths and pointer caches, not a global project index.";
                case "libraryRoot": return "Library Root";
                case "useSelectedFolder": return "Use Selected Folder";
                case "pingDataAsset": return "Ping Database";
                case "settingsNote": return "The unused-prefab feature scans only t:Prefab under this folder.";
                case "strictAvatarInstanceSelection": return "Only select instances under this Avatar";
                case "resetDashboardSettings": return "Reset Dashboard Settings";
                case "dashboardSettingsReset": return "Plugin settings and caches reset.";
                case "resetAllConfirm": return "Reset all persisted settings and caches? This cannot be undone.";
                case "resetConfirmButton": return "Confirm Reset";
                case "details": return "Details";
                case "noSelectedAsset": return "No selected asset.";
                case "ping": return "Ping";
                case "reveal": return "Reveal";
                case "copyPath": return "Copy Path";
                case "pathCopied": return "Path copied.";
                case "categoryActions": return "Category Actions";
                case "addToCurrentCategory": return "Add to Current Category";
                case "removeFromCurrentCategory": return "Remove from Current Category";
                case "addedOneAsset": return "Added to current category.";
                case "tags": return "Tags";
                case "tagsHint": return "Comma separated";
                case "localCover": return "Local Cover Mapping";
                case "coverAsset": return "Cover Image";
                case "clearCover": return "Clear Cover";
                case "boothInfo": return "Booth Info";
                case "boothHidden": return "Booth info is hidden for this asset type. Only prefab assets or folders/packages show Booth link, price, author, etc.";
                case "boothUrl": return "Booth URL";
                case "boothId": return "Booth ID";
                case "boothTitle": return "Title";
                case "boothAuthor": return "Author";
                case "boothPrice": return "Price";
                case "notes": return "Notes";
                case "boothThumbnailUrl": return "Thumbnail URL";
                case "autoFillBooth": return "Auto Fill Online";
                case "fetchingBooth": return "Fetching Booth info...";
                case "boothAutoHint": return "Add a Booth URL/ID for best results. If empty, the tool tries searching by asset name.";
                case "needBoothUrl": return "Add a Booth URL/ID, or rename the asset to something searchable.";
                case "boothFetchFailed": return "Failed to fetch Booth info";
                case "boothFetchDone": return "Booth info updated.";
                case "avatarCardHint": return "Click to select the Avatar; right-click for actions; drag it to Hierarchy.";
                case "dashboardSettings": return "Common Settings";
                case "openBooth": return "Open Booth";
                case "invalidAvatarIgnored": return "Invalid Avatar ignored.";
                case "avatarSelected": return "Avatar selected.";
                case "needAvatar": return "Please select an Avatar first.";
                case "analysisDone": return "Search complete: {0} prefabs found.";
                case "partDetectDone": return "Part detect: {0} entries.";
                case "analysisFailed": return "Search failed";
                case "cacheLoaded": return "Loaded pointer cache for this Avatar.";
                case "invalidLibraryRoot": return "Invalid asset folder. Please update it on Dashboard.";
                case "unusedBuilt": return "Unused prefab list built: {0}.";
                case "unusedCacheEmpty": return "No unused-prefab cache yet. Go to Avatar Select and click Show Prefabs Not Used By Current Avatar.";
                case "categoryOrderUpdated": return "Category order updated.";
                case "viewDetails": return "View Details";
                case "addToCategoryMenu": return "Add to Category";
                case "deleteAsset": return "Delete Asset";
                case "assetFolderSettings": return "Asset Folder";
                case "selectFolderFirst": return "Select a folder in Project first.";
                case "libraryRootHint": return "Unused Prefab scans only this folder; no global index is built.";
                case "recentImportDetected": return "Recent imported assets detected";
                case "recentImportSummary": return "Recently imported/changed: {0} assets | {1}";
                case "targetCategory": return "Target Category";
                case "addImportedToCategory": return "Add to Category";
                case "ignore": return "Ignore";
                case "settingsMovedLibraryRoot": return "Common settings now live under the three Dashboard buttons.";
                case "dragAssetsTitle": return "Drag {0} assets";
                case "multiSelected": return "{0} assets selected";
                case "multiSelectedSummary": return "Prefabs: {0} | Other assets: {1}";
                case "clearSelection": return "Clear Selection";
                case "instantiateToScene": return "Add to Scene";
                case "noPrefabToInstantiate": return "No selected Prefab can be added to the scene.";
                case "instantiatedToScene": return "Added {0} Prefabs to the scene.";
                case "deleteAssetsConfirm": return "This will delete {0} real project assets. Continue?";
                case "deleteAssetConfirm": return "This will delete the real asset from the project. Continue?";
                default: return TC(key);
            }
        }
    }
}
