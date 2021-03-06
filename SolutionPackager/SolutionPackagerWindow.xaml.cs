﻿using CrmDeveloperExtensions2.Core;
using CrmDeveloperExtensions2.Core.Connection;
using CrmDeveloperExtensions2.Core.Enums;
using CrmDeveloperExtensions2.Core.Logging;
using CrmDeveloperExtensions2.Core.Models;
using CrmDeveloperExtensions2.Core.Vs;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.Win32;
using Microsoft.Xrm.Sdk;
using NLog;
using SolutionPackager.Models;
using SolutionPackager.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SolutionType = CrmDeveloperExtensions2.Core.Enums.SolutionType;
using Task = System.Threading.Tasks.Task;
using Window = EnvDTE.Window;

namespace SolutionPackager
{
    public partial class SolutionPackagerWindow : INotifyPropertyChanged
    {
        private readonly DTE _dte;
        private readonly Solution _solution;
        private static readonly Logger ExtensionLogger = LogManager.GetCurrentClassLogger();
        private ObservableCollection<CrmSolution> _solutionData;
        private ObservableCollection<string> _projectFolders;

        public bool SolutionXmlExists;
        public ObservableCollection<CrmSolution> SolutionData
        {
            get => _solutionData;
            set
            {
                _solutionData = value;
                OnPropertyChanged();
            }
        }
        public ObservableCollection<string> ProjectFolders
        {
            get => _projectFolders;
            set
            {
                _projectFolders = value;
                OnPropertyChanged();
            }
        }
        public List<SolutionType> PackageTypes => Enum.GetValues(typeof(SolutionType)).Cast<SolutionType>().ToList();
        public string Command;

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public SolutionPackagerWindow()
        {
            InitializeComponent();
            DataContext = this;

            SolutionData = new ObservableCollection<CrmSolution>();
            ProjectFolders = new ObservableCollection<string>();

            _dte = Package.GetGlobalService(typeof(DTE)) as DTE;
            if (_dte == null)
                return;

            _solution = _dte.Solution;
            if (_solution == null)
                return;

            var events = _dte.Events;
            var windowEvents = events.WindowEvents;
            windowEvents.WindowActivated += WindowEventsOnWindowActivated;

            DataObject.AddPastingHandler(VersionMajor, TextBoxPasting);
            DataObject.AddPastingHandler(VersionMinor, TextBoxPasting);
            DataObject.AddPastingHandler(VersionBuild, TextBoxPasting);
            DataObject.AddPastingHandler(VersionRevision, TextBoxPasting);
        }

        private void WindowEventsOnWindowActivated(Window gotFocus, Window lostFocus)
        {
            //No solution loaded
            if (_solution.Count == 0)
            {
                ResetForm();
                return;
            }

            //WindowEventsOnWindowActivated in this project can be called when activating another window
            //so we don't want to contine further unless our window is active
            if (!HostWindow.IsCrmDevExWindow(gotFocus))
                return;

            //Window was already loaded
            if (SolutionData != null)
                return;

            if (ConnPane.CrmService != null && ConnPane.CrmService.IsReady)
            {
                SetWindowCaption(gotFocus.Caption);
                SetControlState(true);
                BindPackageButton();
                LoadData();
            }
        }

        private async void LoadData()
        {
            GetProjectFolders();
            await GetCrmData();
        }

        private void BindPackageButton()
        {
            string packageFolder = "/";
            if (PackageFolder.SelectedItem != null)
                packageFolder = PackageFolder.SelectedItem.ToString();

            SolutionXmlExists = SolutionXml.SolutionXmlExists(ConnPane.SelectedProject, packageFolder) && SolutionData != null;
        }

        private void GetProjectFolders()
        {
            ProjectFolders = ProjectWorker.GetProjectFolders(ConnPane.SelectedProject, ProjectType.SolutionPackage);

            SetFormDefaults();
        }

        private void SetFormDefaults()
        {
            PackageType.SelectedItem = SolutionType.Unmanaged;
            PackageFolder.SelectedItem = ProjectFolders.FirstOrDefault(p => p == $"/{ExtensionConstants.DefaultPacakgeFolder}");
            SolutionFolder.SelectedItem = ProjectFolders.FirstOrDefault(p => p == "/");
            EnableSolutionPackagerLog.IsChecked = false;
            SaveSolutions.IsChecked = false;
            PublishAll.IsChecked = false;
        }

        private void SetWindowCaption(string currentCaption)
        {
            _dte.ActiveWindow.Caption = HostWindow.SetCaption(currentCaption, ConnPane.CrmService);
        }

        private void ConnPane_OnConnected(object sender, ConnectEventArgs e)
        {
            SetControlState(true);

            LoadData();

            SetWindowCaption(_dte.ActiveWindow.Caption);
        }

        private void ConnPane_OnSolutionBeforeClosing(object sender, EventArgs e)
        {
            ResetForm();

            ClearConnection();
        }

        private void ConnPane_OnSolutionOpened(object sender, EventArgs e)
        {
            ClearConnection();
        }

        private void ClearConnection()
        {
            ConnPane.IsConnected = false;
            ConnPane.CrmService?.Dispose();
            ConnPane.CrmService = null;
        }

        private void ConnPane_OnSolutionProjectRemoved(object sender, SolutionProjectRemovedEventArgs e)
        {
            Project project = e.Project;
            if (ConnPane.SelectedProject == project)
                ResetForm();
        }

        private void SetControlState(bool enabled)
        {
            if (enabled == false)
                PackageSolution.IsEnabled = false;
            SolutionList.IsEnabled = enabled;
            SaveSolutions.IsEnabled = enabled;
            PackageFolder.IsEnabled = enabled;
            PackageType.IsEnabled = enabled;
            EnableSolutionPackagerLog.IsEnabled = enabled;
            SolutionName.IsEnabled = enabled;
            VersionMajor.IsEnabled = enabled;
            VersionMinor.IsEnabled = enabled;
            VersionBuild.IsEnabled = enabled;
            VersionRevision.IsEnabled = enabled;
            UpdateVersion.IsEnabled = enabled;
            PublishAll.IsEnabled = enabled;
        }

        private void ResetForm()
        {
            RemoveEventHandlers();
            SolutionData = new ObservableCollection<CrmSolution>();
            ProjectFolders = new ObservableCollection<string>();
            SaveSolutions.IsChecked = false;
            SetControlState(false);
        }

        private async Task GetCrmData()
        {
            try
            {
                Overlay.ShowMessage(_dte, "Getting CRM data...", vsStatusAnimation.vsStatusAnimationSync);

                var solutionTask = GetSolutions();

                await Task.WhenAll(solutionTask);

                if (!solutionTask.Result)
                {
                    Overlay.HideMessage(_dte, vsStatusAnimation.vsStatusAnimationSync);
                    MessageBox.Show("Error Retrieving Solutions. See the Output Window for additional details.");
                }

                AddEventHandlers();
            }
            finally
            {
                Overlay.HideMessage(_dte, vsStatusAnimation.vsStatusAnimationSync);
            }
        }

        private async Task<bool> GetSolutions()
        {
            EntityCollection results = await Task.Run(() => Crm.Solution.RetrieveSolutionsFromCrm(ConnPane.CrmService));
            if (results == null)
                return false;

            OutputLogger.WriteToOutputWindow("Retrieved Solutions From CRM", MessageType.Info);

            SolutionData = ModelBuilder.CreateCrmSolutionView(results);
            SolutionList.DisplayMemberPath = "NameVersion";

            SolutionPackageConfig solutionPackageConfig = Config.Mapping.GetSolutionPackageConfig(ConnPane.SelectedProject, ConnPane.SelectedProfile, SolutionData);
            if (solutionPackageConfig == null)
                return true;

            SetControlStateForItem(solutionPackageConfig);

            return true;
        }

        private void SetControlStateForItem(SolutionPackageConfig solutionPackageConfig)
        {
            string projectFolder = ProjectFolders.FirstOrDefault(s => s == $"/{solutionPackageConfig.packagepath}");
            SolutionList.SelectedItem = SolutionData.FirstOrDefault(s => s.UniqueName == solutionPackageConfig.solution_uniquename);
            PackageFolder.SelectedItem = projectFolder;
            PackageType.SelectedItem = PackageTypes.FirstOrDefault(s => s.ToString().Equals(solutionPackageConfig.packagetype, StringComparison.InvariantCultureIgnoreCase));
            SolutionName.Text = SetSolutionName(solutionPackageConfig);

            PackageSolution.IsEnabled = SolutionXml.SolutionXmlExists(ConnPane.SelectedProject, projectFolder);
            if (PackageSolution.IsEnabled)
                SetFormVersionNumbers();
        }

        private string SetSolutionName(SolutionPackageConfig solutionPackageConfig)
        {
            if (string.IsNullOrEmpty(solutionPackageConfig.solutionpath))
            {
                return SolutionList.SelectedItem != null
                    ? ((CrmSolution)SolutionList.SelectedItem).Name
                    : string.Empty;
            }

            string[] nameParts = solutionPackageConfig.solutionpath.Split('_');
            return nameParts.Length > 0
                ? nameParts[0]
                : string.Empty;
        }

        private void ImportSolution_OnClick(object sender, RoutedEventArgs e)
        {
            PublishSolutionToCrm();
        }

        private async void PublishSolutionToCrm()
        {
            string latestSolutionPath =
                SolutionXml.GetLatestSolutionPath(ConnPane.SelectedProject, SolutionFolder.SelectedItem.ToString());

            if (string.IsNullOrEmpty(latestSolutionPath))
            {
                OpenFileDialog fileDialog = new OpenFileDialog
                {
                    InitialDirectory = ProjectWorker.GetProjectPath(ConnPane.SelectedProject),
                    Filter = "Solution Files|*.zip;"
                };
                bool? fileResult = fileDialog.ShowDialog();
                if (!fileResult.HasValue || fileResult.Value == false)
                    return;

                latestSolutionPath = fileDialog.FileName;
            }

            bool publishAll = PublishAll.IsChecked == true;
            string publishMesasge = publishAll ? " & publish" : String.Empty;

            MessageBoxResult result = MessageBox.Show($"Are you sure you want to import{publishMesasge} solution?", $"Ok to import{publishMesasge}?",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);

            if (result == MessageBoxResult.No)
                return;

            if (string.IsNullOrEmpty(latestSolutionPath))
            {
                MessageBox.Show("Unable to find solution.");
                return;
            }

            bool success;
            try
            {
                Overlay.ShowMessage(_dte, "Importing solution...", vsStatusAnimation.vsStatusAnimationDeploy);

                success = await Task.Run(() => PublishToCrm(latestSolutionPath, publishAll));
            }
            finally
            {
                Overlay.HideMessage(_dte, vsStatusAnimation.vsStatusAnimationDeploy);
            }

            if (!success)
                MessageBox.Show("Error importing or publishing solution. See output window for details.");

        }

        private async Task<bool> PublishToCrm(string latestSolutionPath, bool publishAll)
        {
            var success = await Task.Run(() => Crm.Solution.ImportSolution(ConnPane.CrmService, latestSolutionPath));

            if (!publishAll)
                return success;

            Overlay.ShowMessage(_dte, "Publishing customizations...", vsStatusAnimation.vsStatusAnimationDeploy);

            success =
                await Task.Run(() => CrmDeveloperExtensions2.Core.Crm.Publish.PublishAllCustomizations(ConnPane.CrmService));

            return success;
        }

        private SolutionPackageConfig CreateMappingObject()
        {
            if (SolutionList.SelectedItem == null)
                return null;

            SolutionPackageConfig solutionPackageConfig = Config.Mapping.GetSolutionPackageConfig(ConnPane.SelectedProject,
                    ConnPane.SelectedProfile, SolutionData);

            return new SolutionPackageConfig
            {
                increment_on_import = solutionPackageConfig.increment_on_import,
                map = solutionPackageConfig.map,
                packagepath = PackageFolder.SelectedItem?.ToString().Replace("/", String.Empty) ?? "",
                profile = ConnPane.SelectedProfile,
                solutionpath = SolutionName.Text,
                packagetype = (SolutionType)PackageType.SelectedItem == SolutionType.Unmanaged
                    ? SolutionType.Unmanaged.ToString().ToLower()
                    : SolutionType.Managed.ToString().ToLower(),
                solution_uniquename = ((CrmSolution)SolutionList.SelectedItem).UniqueName
            };
        }

        private void AddEventHandlers()
        {
            SolutionList.SelectionChanged += SolutionList_OnSelectionChanged;
            PackageFolder.SelectionChanged += PackageFolderOnSelectionChanged;
            PackageType.SelectionChanged += PackageTypeOnSelectionChanged;
            SolutionName.TextChanged += SolutionNameOnTextChanged;
        }
        private void SolutionNameOnTextChanged(object sender, TextChangedEventArgs textChangedEventArgs)
        {
            TriggerMappingUpdate(sender);
        }

        private void PackageTypeOnSelectionChanged(object sender, SelectionChangedEventArgs selectionChangedEventArgs)
        {
            TriggerMappingUpdate(sender);
        }

        private void PackageFolderOnSelectionChanged(object sender, SelectionChangedEventArgs selectionChangedEventArgs)
        {
            TriggerMappingUpdate(sender);
        }

        private void RemoveEventHandlers()
        {
            SolutionList.SelectionChanged -= SolutionList_OnSelectionChanged;
            PackageFolder.SelectionChanged -= PackageFolderOnSelectionChanged;
            PackageType.SelectionChanged -= PackageTypeOnSelectionChanged;
            SolutionName.TextChanged -= SolutionNameOnTextChanged;
        }

        private void SolutionList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!SolutionList.IsLoaded)
                return;

            if (SolutionList.SelectedItem == null)
            {
                Config.Mapping.AddOrUpdateSpklMapping(ConnPane.SelectedProject, ConnPane.SelectedProfile, null);
                return;
            }

            SetControlState(true);
            SetFormVersionNumbers();
            Config.Mapping.AddOrUpdateSpklMapping(ConnPane.SelectedProject, ConnPane.SelectedProfile, CreateMappingObject());
        }

        private void TriggerMappingUpdate(object sender)
        {
            Control c = (Control)sender;
            if (!c.IsLoaded)
                return;

            Config.Mapping.AddOrUpdateSpklMapping(ConnPane.SelectedProject, ConnPane.SelectedProfile, CreateMappingObject());
        }

        private void ConnPane_OnSelectedProjectChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox solutionProjectsList = (ComboBox)e.Source;
            if (!solutionProjectsList.IsLoaded || ConnPane.SelectedProject == null)
                return;

            SolutionPackageConfig solutionPackageConfig = Config.Mapping.GetSolutionPackageConfig(ConnPane.SelectedProject, ConnPane.SelectedProfile, SolutionData);
            if (solutionPackageConfig == null)
                return;

            GetProjectFolders();

            SetControlStateForItem(solutionPackageConfig);
        }

        private void ConnPane_ProfileChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox solutionProjectsList = (ComboBox)e.Source;
            if (!solutionProjectsList.IsLoaded || ConnPane.SelectedProject == null)
                return;

            SolutionPackageConfig solutionPackageConfig = Config.Mapping.GetSolutionPackageConfig(ConnPane.SelectedProject, ConnPane.SelectedProfile, SolutionData);
            if (solutionPackageConfig == null)
                return;

            GetProjectFolders();

            SetControlStateForItem(solutionPackageConfig);
        }

        private void PackageSolution_OnClick(object sender, RoutedEventArgs e)
        {
            PackageProcess();
        }

        private void PackageProcess()
        {
            try
            {
                PackSettings packSettings = GetValuesForPack();

                if (packSettings.Version == null)
                {
                    MessageBox.Show("Invalid Solution.xml version number. See the Output Window for additional details.");
                    return;
                }

                Overlay.ShowMessage(_dte, "Packaging solution...", vsStatusAnimation.vsStatusAnimationSync);

                CommandOutput.Text = String.Empty;

                bool success = ExecutePackage(packSettings);

                if (success)
                    return;

                Overlay.HideMessage(_dte, vsStatusAnimation.vsStatusAnimationSync);
                MessageBox.Show("Error Packaging Solution. See the Output Window for additional details.");
            }
            finally
            {
                Overlay.HideMessage(_dte, vsStatusAnimation.vsStatusAnimationSync);
            }
        }

        private PackSettings GetValuesForPack()
        {
            PackSettings packSettings = new PackSettings
            {
                Project = ConnPane.SelectedProject,
                CrmSolution = (CrmSolution)SolutionList.SelectedItem,
                SolutionPackageConfig = CreateMappingObject(),
                EnablePackagerLogging = EnableSolutionPackagerLog.IsChecked ?? false,
                SaveSolutions = SaveSolutions.IsChecked ?? false,
                SolutionFolder = SolutionFolder.SelectedItem.ToString(),
                ProjectPath = ProjectWorker.GetProjectPath(ConnPane.SelectedProject),
                PackageFolder = PackageFolder.SelectedItem?.ToString() ?? "/"
            };

            packSettings.Version =
                SolutionXml.GetSolutionXmlVersion(ConnPane.SelectedProject, packSettings.PackageFolder.Replace("/", string.Empty));

            packSettings.ProjectSolutionFolder = Path.Combine(packSettings.ProjectPath,
                packSettings.SolutionFolder.Replace("/", string.Empty));

            packSettings.ProjectPackageFolder =
                Path.Combine(packSettings.ProjectPath, packSettings.PackageFolder.Replace("/", string.Empty));

            string solutionName = SolutionName.Text != string.Empty ? SolutionName.Text : "solution";
            packSettings.FileName =
                FileHandler.FormatSolutionVersionString(solutionName, packSettings.Version, false);

            packSettings.FullFilePath = Path.Combine(packSettings.ProjectPackageFolder, packSettings.FileName);

            return packSettings;
        }

        private UnpackSettings GetValuesForUnpack()
        {
            UnpackSettings unpackSettings = new UnpackSettings
            {
                Project = ConnPane.SelectedProject,
                ProjectPath = ProjectWorker.GetProjectPath(ConnPane.SelectedProject),
                CrmSolution = (CrmSolution)SolutionList.SelectedItem,
                SolutionPackageConfig = CreateMappingObject(),
                EnablePackagerLogging = EnableSolutionPackagerLog.IsChecked ?? false,
                SaveSolutions = SaveSolutions.IsChecked ?? false,
                SolutionFolder = SolutionFolder.SelectedItem.ToString(),
                PackageFolder = PackageFolder.SelectedItem?.ToString() ?? "/"
            };

            unpackSettings.ProjectPackageFolder = Path.Combine(unpackSettings.ProjectPath,
                unpackSettings.PackageFolder.Replace("/", string.Empty));

            unpackSettings.ProjectSolutionFolder = Path.Combine(unpackSettings.ProjectPath,
                unpackSettings.SolutionFolder.Replace("/", string.Empty));

            return unpackSettings;
        }

        private string GetToolPath()
        {
            string toolPath = Packager.CreateToolPath(_dte);
            if (!string.IsNullOrEmpty(toolPath))
                return toolPath;

            OutputLogger.WriteToOutputWindow("Unable to find Solution Packager path", MessageType.Error);
            return null;
        }

        private bool ExecutePackage(PackSettings packSettings)
        {
            string toolPath = GetToolPath();
            if (string.IsNullOrEmpty(toolPath))
                return false;

            string commandArgs = Packager.GetPackageCommandArgs(packSettings);
            if (string.IsNullOrEmpty(commandArgs))
            {
                OutputLogger.WriteToOutputWindow("Error creating command arguments", MessageType.Error);
                return false;
            }

            CommandOutput.Text = $"{toolPath} {commandArgs}";

            if (packSettings.SaveSolutions)
                packSettings.FullFilePath = Path.Combine(packSettings.ProjectSolutionFolder, packSettings.FileName);

            return Packager.CreatePackage(_dte, toolPath, packSettings, commandArgs);
        }

        private void UnpackageSolution_OnClick(object sender, RoutedEventArgs e)
        {
            UnpackageProcess();
        }

        private async void UnpackageProcess()
        {
            try
            {
                UnpackSettings unpackSettings = GetValuesForUnpack();

                Overlay.ShowMessage(_dte, "Connecting to CRM/365 and getting unmanaged solution...", vsStatusAnimation.vsStatusAnimationSync);

                List<Task> tasks = new List<Task>();
                var getSolution = Crm.Solution.GetSolutionFromCrm(ConnPane.CrmService, unpackSettings.CrmSolution, unpackSettings.SolutionPackageConfig.packagetype == "managed");
                tasks.Add(getSolution);

                await Task.WhenAll(tasks);

                unpackSettings.DownloadedZipPath = getSolution.Result;

                if (string.IsNullOrEmpty(getSolution.Result))
                {
                    Overlay.HideMessage(_dte, vsStatusAnimation.vsStatusAnimationSync);
                    MessageBox.Show("Error Retrieving Solution. See the Output Window for additional details.");
                    return;
                }

                OutputLogger.WriteToOutputWindow("Retrieved Unmanaged Solution From CRM", MessageType.Info);
                Overlay.ShowMessage(_dte, "Extracting solution...", vsStatusAnimation.vsStatusAnimationSync);

                bool success = ExecuteExtract(unpackSettings);

                if (!success)
                    MessageBox.Show("Error Extracting Solution. See the Output Window for additional details.");

                PackageSolution.IsEnabled = true;
                SetFormVersionNumbers();
            }
            finally
            {
                Overlay.HideMessage(_dte, vsStatusAnimation.vsStatusAnimationSync);
            }
        }

        private bool ExecuteExtract(UnpackSettings unpackSettings)
        {
            string toolPath = GetToolPath();
            if (string.IsNullOrEmpty(toolPath))
                return false;

            unpackSettings.ExtractedFolder = FileHandler.CreateExtractFolder(unpackSettings.DownloadedZipPath);
            if (unpackSettings.ExtractedFolder == null)
                return false;

            string commandArgs = Packager.GetExtractCommandArgs(unpackSettings);

            string command = $"{toolPath} {commandArgs}";
            if (unpackSettings.SaveSolutions)
                command = command.Replace(unpackSettings.ExtractedFolder.FullName, unpackSettings.ProjectPath);

            CommandOutput.Text = command;

            bool success = Packager.ExtractPackage(_dte, toolPath, unpackSettings, commandArgs);

            return success;
        }

        private void ConnPane_OnProjectItemAdded(object sender, ProjectItemAddedEventArgs e)
        {
            BindPackageButton();

            ProjectItem projectItem = e.ProjectItem;
            Guid itemType = new Guid(projectItem.Kind);

            if (itemType != VSConstants.GUID_ItemType_PhysicalFolder)
                return;

            var projectPath = Path.GetDirectoryName(projectItem.ContainingProject.FullName);
            if (projectPath == null) return;

            string newItemName = FileSystem.LocalPathToCrmPath(projectPath, projectItem.FileNames[1]).TrimEnd('/');
            ProjectFolders.Add(newItemName);

            ProjectFolders = new ObservableCollection<string>(ProjectFolders.OrderBy(s => s));
        }

        private void ConnPane_OnProjectItemRemoved(object sender, ProjectItemRemovedEventArgs e)
        {
            BindPackageButton();

            ProjectItem projectItem = e.ProjectItem;

            var projectPath = Path.GetDirectoryName(projectItem.ContainingProject.FullName);
            if (projectPath == null) return;

            Guid itemType = new Guid(projectItem.Kind);

            if (itemType != VSConstants.GUID_ItemType_PhysicalFolder)
                return;

            var itemName = FileSystem.LocalPathToCrmPath(projectPath, projectItem.FileNames[1]);

            ProjectFolders.Remove(itemName);

            ProjectFolders = new ObservableCollection<string>(ProjectFolders.OrderBy(s => s));
        }

        private void ConnPane_OnProjectItemRenamed(object sender, ProjectItemRenamedEventArgs e)
        {
            BindPackageButton();

            ProjectItem projectItem = e.ProjectItem;
            if (projectItem.Name == null)
                return;

            var projectPath = Path.GetDirectoryName(projectItem.ContainingProject.FullName);
            if (projectPath == null)
                return;

            string oldName = e.OldName;
            Guid itemType = new Guid(projectItem.Kind);

            if (itemType != VSConstants.GUID_ItemType_PhysicalFolder)
                return;

            var newItemPath = FileSystem.LocalPathToCrmPath(projectPath, projectItem.FileNames[1]);

            int index = newItemPath.LastIndexOf(projectItem.Name, StringComparison.Ordinal);
            if (index == -1) return;

            var oldItemPath = newItemPath.Remove(index, projectItem.Name.Length).Insert(index, oldName);

            ProjectFolders.Remove(oldItemPath);

            ProjectFolders = new ObservableCollection<string>(ProjectFolders.OrderBy(s => s));
        }

        private void SetFormVersionNumbers()
        {
            if (PackageFolder.SelectedItem == null)
                return;

            Version version = SolutionXml.GetSolutionXmlVersion(ConnPane.SelectedProject, PackageFolder.SelectedItem.ToString());
            if (version == null)
            {
                VersionMajor.Text = String.Empty;
                VersionMinor.Text = String.Empty;
                VersionBuild.Text = String.Empty;
                VersionRevision.Text = String.Empty;
                return;
            }

            VersionMajor.Text = version.Major.ToString();
            VersionMinor.Text = version.Minor.ToString();
            VersionBuild.Text = version.Build != -1 ? version.Build.ToString() : String.Empty;
            VersionRevision.Text = version.Revision != -1 ? version.Revision.ToString() : String.Empty;
        }

        private void UpdateVersion_OnClick(object sender, RoutedEventArgs e)
        {
            UpdateSolutionVersion();
        }

        private async void UpdateSolutionVersion()
        {
            try
            {
                Version version = Versioning.ValidateVersionInput(VersionMajor.Text, VersionMinor.Text,
                    VersionBuild.Text, VersionRevision.Text);
                if (version == null)
                {
                    MessageBox.Show("Invalid version number");
                    return;
                }

                Overlay.ShowMessage(_dte, "Updating");

                bool success = SolutionXml.SetSolutionXmlVersion(ConnPane.SelectedProject, version, PackageFolder.SelectedItem.ToString());
                if (!success)
                {
                    Overlay.HideMessage(_dte);
                    MessageBox.Show("Error updating Solution.xml version: see output window for details");
                    return;
                }

                Overlay.ShowMessage(_dte, "Updated");

                await Task.Delay(500);
            }
            finally
            {
                Overlay.HideMessage(_dte);
            }
        }

        private void Version_OnPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !IsTextAllowed(e.Text);
        }

        private static bool IsTextAllowed(string text)
        {
            Regex regex = new Regex("[^0-9.-]+");
            return !regex.IsMatch(text);
        }

        private static void TextBoxPasting(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(typeof(String)))
            {
                String text = (String)e.DataObject.GetData(typeof(String));
                if (!IsTextAllowed(text))
                    e.CancelCommand();
            }
            else
                e.CancelCommand();
        }
    }
}