using Stylet;
using System;
using System.Collections.ObjectModel;
using System.IO;
using XboxCsMgr.Client.ViewModels.Controls;
using XboxCsMgr.XboxLive;
using XboxCsMgr.XboxLive.Model.TitleStorage;
using XboxCsMgr.XboxLive.Services;

namespace XboxCsMgr.Client.ViewModels
{
    public class SaveViewModel : Screen
    {
        private IEventAggregator _events;
        private XboxLiveConfig _xblConfig;
        private TitleStorageService _storageService;

        private string _packageFamilyName;
        public string PackageFamilyName
        {
            get => _packageFamilyName;
            set
            {
                _packageFamilyName = value;
            }
        }

        private string _serviceConfigurationId;
        public string ServiceConfigurationId
        {
            get => _serviceConfigurationId;
            set
            {
                _serviceConfigurationId = value;
            }
        }

        private ObservableCollection<SavedBlobsViewModel> _saveData;
        public ObservableCollection<SavedBlobsViewModel> SaveData
        {
            get => _saveData;
        }

        public SavedAtomsViewModel? SelectedAtom
        {
            get;
            set;
        }

        public SaveViewModel(IEventAggregator events, XboxLiveConfig config, string pfn, string scid)
        {
            _events = events;
            _xblConfig = config;
            _packageFamilyName = pfn;
            _serviceConfigurationId = scid;

            _storageService = new TitleStorageService(_xblConfig, _packageFamilyName, _serviceConfigurationId);
            _saveData = new ObservableCollection<SavedBlobsViewModel>();

            FetchSaveMetadata();
        }

        private async void FetchSaveMetadata()
        {
            TitleStorageBlobMetadataResult blobMetadataResult = await _storageService.GetBlobMetadata();
            if (blobMetadataResult != null && blobMetadataResult.Blobs != null)
            {
                foreach (TitleStorageBlobMetadata entry in blobMetadataResult.Blobs)
                {
                    _saveData.Add(new SavedBlobsViewModel(_storageService, entry));
                }
            }
        }

        public void SelectedItemChanged(object args)
        {
            SelectedAtom = args as SavedAtomsViewModel;
        }

        public async void Download()
        {
            if (SelectedAtom == null)
                return;

            string atom = SelectedAtom.AtomValue;
            Microsoft.Win32.SaveFileDialog dlg = new Microsoft.Win32.SaveFileDialog();
            dlg.FileName = SelectedAtom.AtomName;

            bool? res = dlg.ShowDialog();
            if (res == true)
            {
                byte[] atomData = await _storageService.DownloadAtomAsync(atom);
                await File.WriteAllBytesAsync(dlg.FileName, atomData);
            }
        }

        public async void Upload()
        {
            if (SelectedAtom == null)
            {
                System.Windows.MessageBox.Show(
                    "Select an atom first: expand a save slot in the tree, then right-click the atom inside it.",
                    "No atom selected", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            // AtomValue format: "{UUID},binary" — strip ",binary" to get the raw UUID
            string atomUuid = SelectedAtom.AtomValue.Contains(',')
                ? SelectedAtom.AtomValue.Substring(0, SelectedAtom.AtomValue.IndexOf(','))
                : SelectedAtom.AtomValue;

            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title  = $"Select replacement file for {SelectedAtom.AtomName}",
                Filter = "Save files (*.bin)|*.bin|All files (*.*)|*.*"
            };

            if (dlg.ShowDialog() != true) return;

            byte[] atomData = await File.ReadAllBytesAsync(dlg.FileName);

            // Build a minimal metadata object — UploadBlobAsync checks if this is null and returns early if so
            var blobMeta = new XboxCsMgr.XboxLive.Model.TitleStorage.TitleStorageBlobMetadata
            {
                FileName    = SelectedAtom.AtomName,
                DisplayName = SelectedAtom.AtomName,
                Size        = (ulong)atomData.Length,
            };

            try
            {
                var response = await _storageService.UploadBlobAsync(blobMeta, atomData, atomUuid);
                if (response != null && (int)response.StatusCode < 300)
                {
                    System.Windows.MessageBox.Show(
                        $"✔ Upload successful!\n\nAtom: {SelectedAtom.AtomName}\nUUID: {atomUuid}\nSize: {atomData.Length:N0} bytes\n\nLaunch Dead Island DE on Xbox and load your save.",
                        "Upload Complete", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
                else
                {
                    string status = response != null ? $"{(int)response.StatusCode} {response.ReasonPhrase}" : "null response";
                    System.Windows.MessageBox.Show(
                        $"Upload may have failed — server response: {status}\n\nIf this persists, try signing out and back in.",
                        "Upload Warning", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Upload failed:\n{ex.Message}",
                    "Upload Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
    }
}
