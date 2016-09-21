﻿// ------------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All Rights Reserved.  Licensed under the MIT License.  See License in the project root for license information.
// ------------------------------------------------------------------------------

namespace OneDriveApiBrowser
{
    using Microsoft.Graph;
    using Microsoft.OneDrive.Sdk;
    using Microsoft.OneDrive.Sdk.Authentication;
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using System.Windows.Forms;

    public partial class FormBrowser : Form
    {
        private const string AadClientId = "Insert your AAD client ID here";
        private const string MsaClientId = "Insert your MSA client ID here";

        public const string AadReturnUrl = "Insert your AAD return URL here";
        public const string AadTargetUrl = "Insert your AAD target URL here";
        public const string MsaReturnUrl = "https://login.live.com/oauth20_desktop.srf";

        private static readonly string[] Scopes = { "onedrive.readwrite", "wl.signin" };

        private enum ClientType
        {
            Consumer,
            Business
        }

        private const int UploadChunkSize = 10 * 1024 * 1024;       // 10 MB
        private IOneDriveClient oneDriveClient { get; set; }
        private ClientType clientType { get; set; }
        private Item CurrentFolder { get; set; }
        private Item SelectedItem { get; set; }

        private OneDriveTile _selectedTile;

        public FormBrowser()
        {
            InitializeComponent();
        }

        private void ShowWork(bool working)
        {
            this.UseWaitCursor = working;
            this.progressBar1.Visible = working;

        }

        private async Task LoadFolderFromId(string id)
        {
            if (null == this.oneDriveClient) return;

            // Update the UI for loading something new
            ShowWork(true);
            LoadChildren(new Item[0]);

            try
            {
                var expandString = this.clientType == ClientType.Consumer
                    ? "thumbnails,children(expand=thumbnails)"
                    : "thumbnails,children";

                var folder =
                    await this.oneDriveClient.Drive.Items[id].Request().Expand(expandString).GetAsync();

                ProcessFolder(folder);
            }
            catch (Exception exception)
            {
                PresentServiceException(exception);
            }

            ShowWork(false);
        }

        private async Task LoadFolderFromPath(string path = null)
        {
            if (null == this.oneDriveClient) return;

            // Update the UI for loading something new
            ShowWork(true);
            LoadChildren(new Item[0]);

            try
            {
                Item folder;

                var expandValue = this.clientType == ClientType.Consumer
                    ? "thumbnails,children(expand=thumbnails)"
                    : "thumbnails,children";

                if (path == null)
                {
                    folder = await this.oneDriveClient.Drive.Root.Request().Expand(expandValue).GetAsync();
                }
                else
                {
                    folder =
                        await
                            this.oneDriveClient.Drive.Root.ItemWithPath("/" + path)
                                .Request()
                                .Expand(expandValue)
                                .GetAsync();
                }

                ProcessFolder(folder);
            }
            catch (Exception exception)
            {
                PresentServiceException(exception);
            }

            ShowWork(false);
        }

        private void ProcessFolder(Item folder)
        {
            if (folder != null)
            {
                this.CurrentFolder = folder;

                LoadProperties(folder);

                if (folder.Folder != null && folder.Children != null && folder.Children.CurrentPage != null)
                {
                    LoadChildren(folder.Children.CurrentPage);
                }
            }
        }

        private void LoadProperties(Item item)
        {
            this.SelectedItem = item;
            objectBrowser.SelectedItem = item;
        }

        private void LoadChildren(IList<Item> items)
        {
            flowLayoutContents.SuspendLayout();
            flowLayoutContents.Controls.Clear();

            // Load the children
            foreach (var obj in items)
            {
                AddItemToFolderContents(obj);
            }

            flowLayoutContents.ResumeLayout();
        }

        private void AddItemToFolderContents(Item obj)
        {
            flowLayoutContents.Controls.Add(CreateControlForChildObject(obj));
        }

        private void RemoveItemFromFolderContents(Item itemToDelete)
        {
            flowLayoutContents.Controls.RemoveByKey(itemToDelete.Id);
        }

        private Control CreateControlForChildObject(Item item)
        {
            OneDriveTile tile = new OneDriveTile(this.oneDriveClient);
            tile.SourceItem = item;
            tile.Click += ChildObject_Click;
            tile.DoubleClick += ChildObject_DoubleClick;
            tile.Name = item.Id;
            return tile;
        }

        void ChildObject_DoubleClick(object sender, EventArgs e)
        {
            var item = ((OneDriveTile)sender).SourceItem;

            // Look up the object by ID
            NavigateToFolder(item);
        }
        void ChildObject_Click(object sender, EventArgs e)
        {
            if (null != _selectedTile)
            {
                _selectedTile.Selected = false;
            }
            
            var item = ((OneDriveTile)sender).SourceItem;
            LoadProperties(item);
            _selectedTile = (OneDriveTile)sender;
            _selectedTile.Selected = true;
        }

        private void FormBrowser_Load(object sender, EventArgs e)
        {
            
        }

        private void NavigateToFolder(Item folder)
        {
            Task t = LoadFolderFromId(folder.Id);

            // Fix up the breadcrumbs
            var breadcrumbs = flowLayoutPanelBreadcrumb.Controls;
            bool existingCrumb = false;
            foreach (LinkLabel crumb in breadcrumbs)
            {
                if (crumb.Tag == folder)
                {
                    RemoveDeeperBreadcrumbs(crumb);
                    existingCrumb = true;
                    break;
                }
            }

            if (!existingCrumb)
            {
                LinkLabel label = new LinkLabel();
                label.Text = "> " + folder.Name;
                label.LinkArea = new LinkArea(2, folder.Name.Length);
                label.LinkClicked += linkLabelBreadcrumb_LinkClicked;
                label.AutoSize = true;
                label.Tag = folder;
                flowLayoutPanelBreadcrumb.Controls.Add(label);
            }
        }

        private void linkLabelBreadcrumb_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            LinkLabel link = (LinkLabel)sender;

            RemoveDeeperBreadcrumbs(link);

            Item item = link.Tag as Item;
            if (null == item)
            {

                Task t = LoadFolderFromPath(null);
            }
            else
            {
                Task t = LoadFolderFromId(item.Id);
            }
        }

        private void RemoveDeeperBreadcrumbs(LinkLabel link)
        {
            // Remove the breadcrumbs deeper than this item
            var breadcrumbs = flowLayoutPanelBreadcrumb.Controls;
            int indexOfControl = breadcrumbs.IndexOf(link);
            for (int i = breadcrumbs.Count - 1; i > indexOfControl; i--)
            {
                breadcrumbs.RemoveAt(i);
            }
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void UpdateConnectedStateUx(bool connected)
        {
            signInAadToolStripMenuItem.Visible = !connected;
            signInMsaToolStripMenuItem.Visible = !connected;
            signOutToolStripMenuItem.Visible = connected;
            flowLayoutPanelBreadcrumb.Visible = connected;
            flowLayoutContents.Visible = connected;
        }

        private async void signInAadToolStripMenuItem_Click(object sender, EventArgs e)
        {
            await this.SignIn(ClientType.Business);
        }

        private async void signInMsaToolStripMenuItem_Click(object sender, EventArgs e)
        {
            await this.SignIn(ClientType.Consumer);
        }

        private async Task SignIn(ClientType clientType)
        {
            Task authTask;

            if (clientType == ClientType.Consumer)
            {
                var msaAuthProvider = new MsaAuthenticationProvider(
                    FormBrowser.MsaClientId,
                    null,
                    FormBrowser.MsaReturnUrl,
                    FormBrowser.Scopes,
                    null,
                    new CredentialVault(FormBrowser.MsaClientId));
                this.oneDriveClient = new OneDriveClient("https://api.onedrive.com/v1.0", msaAuthProvider);
                authTask = msaAuthProvider.RestoreMostRecentFromCacheOrAuthenticateUserAsync();
            }
            else
            {
                var adalAuthProvider = new AdalAuthenticationProvider(
                    FormBrowser.AadClientId,
                    FormBrowser.AadReturnUrl);
                this.oneDriveClient = new OneDriveClient(FormBrowser.AadTargetUrl + "/_api/v2.0", adalAuthProvider);
                authTask = adalAuthProvider.AuthenticateUserAsync(FormBrowser.AadTargetUrl);
            }

            try
            {
                await authTask;
            }
            catch (ServiceException exception)
            {
                if (OAuthConstants.ErrorCodes.AuthenticationFailure == exception.Error.Code)
                {
                    MessageBox.Show(
                        "Authentication failed",
                        "Authentication failed",
                        MessageBoxButtons.OK);

                    this.oneDriveClient = null;
                }
                else
                {
                    PresentServiceException(exception);
                }
            }

            this.clientType = clientType;

            try
            {
                await LoadFolderFromPath();

                UpdateConnectedStateUx(true);
            }
            catch (ServiceException exception)
            {
                PresentServiceException(exception);
                this.oneDriveClient = null;
            }
        }

        private void signOutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (this.oneDriveClient != null)
            {
                this.oneDriveClient = null;
            }

            UpdateConnectedStateUx(false);
        }

        private System.IO.Stream GetFileStreamForUpload(string targetFolderName, out string originalFilename)
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "Upload to " + targetFolderName;
            dialog.Filter = "All Files (*.*)|*.*";
            dialog.CheckFileExists = true;
            var response = dialog.ShowDialog();
            if (response != DialogResult.OK)
            {
                originalFilename = null;
                return null;
            }

            try
            {
                originalFilename = System.IO.Path.GetFileName(dialog.FileName);
                return new System.IO.FileStream(dialog.FileName, System.IO.FileMode.Open);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error uploading file: " + ex.Message);
                originalFilename = null;
                return null;
            }
        }

        private async void simpleUploadToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var targetFolder = this.CurrentFolder;

            string filename;
            using (var stream = GetFileStreamForUpload(targetFolder.Name, out filename))
            {
                if (stream != null)
                {
                    string folderPath = targetFolder.ParentReference == null
                        ? "/drive/items/root:"
                        : targetFolder.ParentReference.Path + "/" + Uri.EscapeUriString(targetFolder.Name);
                    var uploadPath = folderPath + "/" + Uri.EscapeUriString(System.IO.Path.GetFileName(filename));

                    try
                    {
                        var uploadedItem =
                            await
                                this.oneDriveClient.ItemWithPath(uploadPath).Content.Request().PutAsync<Item>(stream);

                        AddItemToFolderContents(uploadedItem);

                        MessageBox.Show("Uploaded with ID: " + uploadedItem.Id);
                    }
                    catch (Exception exception)
                    {
                        PresentServiceException(exception);
                    }
                }
            }
        }

        private async void simpleIDbasedToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var targetFolder = this.CurrentFolder;

            string filename;
            using (var stream = GetFileStreamForUpload(targetFolder.Name, out filename))
            {
                if (stream != null)
                {
                    try
                    {
                        var uploadedItem =
                            await
                                this.oneDriveClient.Drive.Items[targetFolder.Id].ItemWithPath(filename).Content.Request()
                                    .PutAsync<Item>(stream);

                        AddItemToFolderContents(uploadedItem);

                        MessageBox.Show("Uploaded with ID: " + uploadedItem.Id);
                    }
                    catch (Exception exception)
                    {
                        PresentServiceException(exception);
                    }
                }
            }
        }

        private async void createFolderToolStripMenuItem_Click(object sender, EventArgs e)
        {
            FormInputDialog dialog = new FormInputDialog("Create Folder", "New folder name:");
            var result = dialog.ShowDialog();
            if (result == System.Windows.Forms.DialogResult.OK && !string.IsNullOrEmpty(dialog.InputText))
            {
                try
                {
                    var folderToCreate = new Item { Name = dialog.InputText, Folder = new Folder() };
                    var newFolder =
                        await this.oneDriveClient.Drive.Items[this.SelectedItem.Id].Children.Request()
                            .AddAsync(folderToCreate);

                    if (newFolder != null)
                    {
                        MessageBox.Show("Created new folder with ID " + newFolder.Id);
                        this.AddItemToFolderContents(newFolder);
                    }
                }
                catch(ServiceException exception)
                {
                    if (exception.IsMatch(OneDriveErrorCode.InvalidRequest.ToString()))
                    {
                        MessageBox.Show(
                            "Please enter a valid folder name.",
                            "Invalid folder name",
                            MessageBoxButtons.OK);

                        dialog.Dispose();
                        this.createFolderToolStripMenuItem_Click(sender, e);
                    }
                    else
                    {
                        PresentServiceException(exception);
                    }
                }
                catch (Exception exception)
                {
                    PresentServiceException(exception);
                }
            }
        }

        private static void PresentServiceException(Exception exception)
        {
            string message = null;
            var oneDriveException = exception as ServiceException;
            if (oneDriveException == null)
            {
                message = exception.Message;
            }
            else
            {
                message = string.Format("{0}{1}", Environment.NewLine, oneDriveException.ToString());
            }

            MessageBox.Show(string.Format("OneDrive reported the following error: {0}", message));
        }

        private async void deleteSelectedItemToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var itemToDelete = this.SelectedItem;
            var result = MessageBox.Show("Are you sure you want to delete " + itemToDelete.Name + "?", "Confirm Delete", MessageBoxButtons.YesNo);
            if (result == System.Windows.Forms.DialogResult.Yes)
            {
                try
                {
                    await this.oneDriveClient.Drive.Items[itemToDelete.Id].Request().DeleteAsync();
                    
                    RemoveItemFromFolderContents(itemToDelete);
                    MessageBox.Show("Item was deleted successfully");
                }
                catch (Exception exception)
                {
                    PresentServiceException(exception);
                }
            }
        }

        private async void getChangesHereToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                var result =
                    await this.oneDriveClient.Drive.Items[this.CurrentFolder.Id].Delta(null).Request().GetAsync();

                Console.WriteLine(result);
            }
            catch (Exception ex)
            {
                PresentServiceException(ex);
            }
        }

        private void menuStrip1_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {

        }

        private async void openFromOneDriveToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var cleanAppId = "0000000040131ABA";
            var result = await OneDriveSamples.Picker.FormOneDrivePicker.OpenFileAsync(cleanAppId, true, this);

            try
            {
                var pickedFilesContainer = await result.GetItemsFromSelectionAsync(this.oneDriveClient);

                ProcessFolder(pickedFilesContainer);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }

            //// Do something with the picker result we got
            //var pickedFiles = await result.GetSelectionResponseAsync();
            //if (pickedFiles == null)
            //{
            //    MessageBox.Show("Error picking files.");
            //    return;
            //}

            //StringBuilder builder = new StringBuilder();
            //builder.AppendFormat("You selected {0} files\n", pickedFiles.Length);
            //foreach (var file in pickedFiles)
            //{
            //    builder.AppendLine(file.name);
            //}

            //MessageBox.Show(builder.ToString());
        }

        private async void saveSelectedFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var item = this.SelectedItem;
            if (null == item)
            {
                MessageBox.Show("Nothing selected.");
                return;
            }

            var dialog = new SaveFileDialog();
            dialog.FileName = item.Name;
            dialog.Filter = "All Files (*.*)|*.*";
            var result = dialog.ShowDialog();
            if (result != System.Windows.Forms.DialogResult.OK)
                return;

            using (var stream = await this.oneDriveClient.Drive.Items[item.Id].Content.Request().GetAsync())
            using (var outputStream = new System.IO.FileStream(dialog.FileName, System.IO.FileMode.Create))
            {
                await stream.CopyToAsync(outputStream);
            }
        }
    }
}
