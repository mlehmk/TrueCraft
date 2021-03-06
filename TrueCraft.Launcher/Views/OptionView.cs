﻿using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using TrueCraft.Core;
using Xwt;
using Xwt.Drawing;

namespace TrueCraft.Launcher.Views
{
    public class OptionView : VBox
    {
        public LauncherWindow Window { get; set; }

        public Label OptionLabel { get; set; }
        public Label ResolutionLabel { get; set; }
        public ComboBox ResolutionComboBox { get; set; }
        public CheckBox FullscreenCheckBox { get; set; }
        public Label TexturePackLabel { get; set; }
        public DataField<Image> TexturePackImageField { get; set; }
        public DataField<string> TexturePackTextField { get; set; }
        public ListStore TexturePackStore { get; set; }
        public ListView TexturePackListView { get; set; }
        public Button OpenFolderButton { get; set; }
        public Button BackButton { get; set; }

        private List<TexturePack> _texturePacks;
        private TexturePack _lastTexturePack;

        public OptionView(LauncherWindow window)
        {
            _texturePacks = new List<TexturePack>();
            _lastTexturePack = null;

            Window = window;
            this.MinWidth = 250;

            OptionLabel = new Label("Options")
            {
                Font = Font.WithSize(16),
                TextAlignment = Alignment.Center
            };

            ResolutionLabel = new Label("Select a resolution...");
            ResolutionComboBox = new ComboBox();

            int resolutionIndex = -1;
            for (int i = 0; i < WindowResolution.Defaults.Length; i++)
            {
                ResolutionComboBox.Items.Add(WindowResolution.Defaults[i].ToString());

                if (resolutionIndex == -1)
                {
                    resolutionIndex =
                        ((WindowResolution.Defaults[i].Width == UserSettings.Local.WindowResolution.Width) &&
                        (WindowResolution.Defaults[i].Height == UserSettings.Local.WindowResolution.Height)) ? i : -1;
                }
            }

            if (resolutionIndex == -1)
            {
                ResolutionComboBox.Items.Add(UserSettings.Local.WindowResolution.ToString());
                resolutionIndex = ResolutionComboBox.Items.Count - 1;
            }

            ResolutionComboBox.SelectedIndex = resolutionIndex;
            FullscreenCheckBox = new CheckBox()
            {
                Label = "Fullscreen mode",
                State = (UserSettings.Local.IsFullscreen) ? CheckBoxState.On : CheckBoxState.Off
            };

            TexturePackLabel = new Label("Select a texture pack...");
            TexturePackImageField = new DataField<Image>();
            TexturePackTextField = new DataField<string>();
            TexturePackStore = new ListStore(TexturePackImageField, TexturePackTextField);
            TexturePackListView = new ListView
            {
                MinHeight = 200,
                SelectionMode = SelectionMode.Single,
                DataSource = TexturePackStore,
                HeadersVisible = false
            };
            OpenFolderButton = new Button("Open texture pack folder");
            BackButton = new Button("Back");

            TexturePackListView.Columns.Add("Image", TexturePackImageField);
            TexturePackListView.Columns.Add("Text", TexturePackTextField);

            ResolutionComboBox.SelectionChanged += (sender, e) =>
            {
                UserSettings.Local.WindowResolution =
                    WindowResolution.FromString(ResolutionComboBox.SelectedText);
                UserSettings.Local.Save();
            };

            FullscreenCheckBox.Clicked += (sender, e) =>
            {
                UserSettings.Local.IsFullscreen = !UserSettings.Local.IsFullscreen;
                UserSettings.Local.Save();
            };

            TexturePackListView.SelectionChanged += (sender, e) =>
            {
                var texturePack = _texturePacks[TexturePackListView.SelectedRow];
                if (_lastTexturePack != texturePack)
                {
                    UserSettings.Local.SelectedTexturePack = texturePack.Name;
                    UserSettings.Local.Save();
                }
            };

            OpenFolderButton.Clicked += (sender, e) =>
            {
                var dir = new DirectoryInfo(TexturePack.TexturePackPath);
                Process.Start(dir.FullName);
            };

            BackButton.Clicked += (sender, e) =>
            {
                Window.MainContainer.Remove(this);
                Window.MainContainer.PackEnd(Window.MainMenuView);
            };

            LoadTexturePacks();

            this.PackStart(OptionLabel);
            this.PackStart(ResolutionLabel);
            this.PackStart(ResolutionComboBox);
            this.PackStart(FullscreenCheckBox);
            this.PackStart(TexturePackLabel);
            this.PackStart(TexturePackListView);
            this.PackStart(OpenFolderButton);
            this.PackEnd(BackButton);
        }

        private void LoadTexturePacks()
        {
            // We load the default texture pack specially.
            _texturePacks.Add(TexturePack.Default);
            AddTexturePackRow(TexturePack.Default);

            // Make sure to create the texture pack directory if there is none.
            if (!Directory.Exists(TexturePack.TexturePackPath))
                Directory.CreateDirectory(TexturePack.TexturePackPath);

            var zips = Directory.EnumerateFiles(TexturePack.TexturePackPath);
            foreach (var zip in zips)
            {
                if (!zip.EndsWith(".zip"))
                    continue;

                var texturePack = TexturePack.FromArchive(zip);
                if (texturePack != null)
                {
                    _texturePacks.Add(texturePack);
                    AddTexturePackRow(texturePack);
                }
            }
        }

        private void AddTexturePackRow(TexturePack pack)
        {
            var row = TexturePackStore.AddRow();

            TexturePackStore.SetValue(row, TexturePackImageField, Image.FromStream(pack.Image).WithSize(IconSize.Medium));
            TexturePackStore.SetValue(row, TexturePackTextField, pack.Name + "\r\n" + pack.Description);
        }
    }
}
