using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using DevExpress.XtraLayout;
using LifeStream.Core.Infrastructure;
using Serilog;

namespace LifeStream.Desktop.Controls;

/// <summary>
/// A generic image browser form that displays thumbnails with a full-size preview.
/// Can be used for browsing any folder of images.
/// </summary>
public class ImageBrowserForm : DevExpress.XtraEditors.XtraForm
{
    private static readonly ILogger Log = LoggingConfig.ForCategory(LoggingConfig.Categories.UI);

    private readonly string _folderPath;
    private readonly string _title;
    private SplitContainerControl _splitContainer = null!;
    private FlowLayoutPanel _thumbnailPanel = null!;
    private PictureEdit _previewPicture = null!;
    private LabelControl _previewLabel = null!;
    private List<string> _imageFiles = new();
    private string? _selectedImage;

    /// <summary>
    /// Event raised when an image is selected.
    /// </summary>
    public event EventHandler<string>? ImageSelected;

    /// <summary>
    /// Creates a new image browser form.
    /// </summary>
    /// <param name="folderPath">Path to the folder containing images.</param>
    /// <param name="title">Title for the form.</param>
    public ImageBrowserForm(string folderPath, string title = "Image Browser")
    {
        _folderPath = folderPath;
        _title = title;
        InitializeComponents();
        LoadImages();
    }

    private void InitializeComponents()
    {
        Text = _title;
        Size = new Size(1000, 700);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = true;
        FormBorderStyle = FormBorderStyle.Sizable;

        // Split container: thumbnails on left, preview on right
        _splitContainer = new SplitContainerControl
        {
            Dock = DockStyle.Fill,
            Horizontal = true,
            SplitterPosition = 300,
            FixedPanel = SplitFixedPanel.Panel1
        };

        // Left panel: scrollable thumbnail grid
        var thumbnailContainer = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true
        };

        _thumbnailPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(5)
        };

        thumbnailContainer.Controls.Add(_thumbnailPanel);
        _splitContainer.Panel1.Controls.Add(thumbnailContainer);

        // Right panel: preview
        var previewLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(8)
        };
        previewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // Image
        previewLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // Label

        _previewPicture = new PictureEdit
        {
            Dock = DockStyle.Fill,
            Properties = { SizeMode = DevExpress.XtraEditors.Controls.PictureSizeMode.Zoom }
        };
        _previewPicture.Properties.ShowCameraMenuItem = DevExpress.XtraEditors.Controls.CameraMenuItemVisibility.Never;
        _previewPicture.DoubleClick += OnPreviewDoubleClick;
        previewLayout.Controls.Add(_previewPicture, 0, 0);

        _previewLabel = new LabelControl
        {
            Text = "Select an image",
            Dock = DockStyle.Fill,
            AutoSizeMode = LabelAutoSizeMode.None,
            Appearance = { TextOptions = { HAlignment = DevExpress.Utils.HorzAlignment.Center } }
        };
        previewLayout.Controls.Add(_previewLabel, 0, 1);

        _splitContainer.Panel2.Controls.Add(previewLayout);

        // Button panel at bottom
        var buttonPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            Padding = new Padding(8)
        };

        var selectButton = new SimpleButton
        {
            Text = "Select",
            Dock = DockStyle.Right,
            Width = 80,
            DialogResult = DialogResult.OK
        };
        selectButton.Click += OnSelectClick;

        var cancelButton = new SimpleButton
        {
            Text = "Cancel",
            Dock = DockStyle.Right,
            Width = 80,
            DialogResult = DialogResult.Cancel
        };

        buttonPanel.Controls.Add(cancelButton);
        buttonPanel.Controls.Add(selectButton);

        Controls.Add(_splitContainer);
        Controls.Add(buttonPanel);

        AcceptButton = selectButton;
        CancelButton = cancelButton;
    }

    private void LoadImages()
    {
        if (!Directory.Exists(_folderPath))
        {
            Log.Warning("Image folder does not exist: {Path}", _folderPath);
            return;
        }

        // Find all image files
        var extensions = new[] { "*.jpg", "*.jpeg", "*.png", "*.gif", "*.bmp" };
        _imageFiles = extensions
            .SelectMany(ext => Directory.GetFiles(_folderPath, ext))
            .OrderByDescending(f => File.GetLastWriteTime(f))
            .ToList();

        Log.Information("Found {Count} images in {Path}", _imageFiles.Count, _folderPath);

        // Create thumbnails
        foreach (var file in _imageFiles)
        {
            CreateThumbnail(file);
        }
    }

    private void CreateThumbnail(string filePath)
    {
        try
        {
            var thumbnail = new PictureEdit
            {
                Size = new Size(120, 90),
                Margin = new Padding(3),
                Cursor = Cursors.Hand,
                Properties = { SizeMode = DevExpress.XtraEditors.Controls.PictureSizeMode.Zoom }
            };
            thumbnail.Properties.ShowCameraMenuItem = DevExpress.XtraEditors.Controls.CameraMenuItemVisibility.Never;
            thumbnail.Tag = filePath;

            // Load thumbnail asynchronously
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                    using var originalImage = Image.FromStream(stream);

                    // Create a smaller thumbnail
                    var thumbWidth = 120;
                    var thumbHeight = (int)(originalImage.Height * (thumbWidth / (float)originalImage.Width));
                    var thumb = originalImage.GetThumbnailImage(thumbWidth, Math.Min(thumbHeight, 90), () => false, IntPtr.Zero);

                    // Update on UI thread
                    if (!IsDisposed && !thumbnail.IsDisposed)
                    {
                        BeginInvoke(new Action(() =>
                        {
                            if (!IsDisposed && !thumbnail.IsDisposed)
                            {
                                thumbnail.Image = thumb;
                            }
                        }));
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug("Failed to load thumbnail for {Path}: {Error}", filePath, ex.Message);
                }
            });

            thumbnail.Click += (s, e) => SelectImage(filePath);
            thumbnail.DoubleClick += (s, e) =>
            {
                SelectImage(filePath);
                OnSelectClick(s, e);
            };

            _thumbnailPanel.Controls.Add(thumbnail);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to create thumbnail for {Path}", filePath);
        }
    }

    private void SelectImage(string filePath)
    {
        _selectedImage = filePath;

        // Update preview
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            _previewPicture.Image = Image.FromStream(stream);

            var fileName = Path.GetFileName(filePath);
            var fileInfo = new FileInfo(filePath);
            _previewLabel.Text = $"{fileName} | {fileInfo.LastWriteTime:yyyy-MM-dd HH:mm} | {fileInfo.Length / 1024:N0} KB";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load preview for {Path}", filePath);
            _previewLabel.Text = "Failed to load preview";
        }

        // Highlight selected thumbnail
        foreach (Control control in _thumbnailPanel.Controls)
        {
            if (control is PictureEdit pic)
            {
                pic.BackColor = pic.Tag as string == filePath
                    ? Color.FromArgb(60, 100, 180, 255)
                    : Color.Transparent;
            }
        }
    }

    private void OnPreviewDoubleClick(object? sender, EventArgs e)
    {
        if (_selectedImage != null)
        {
            OnSelectClick(sender, e);
        }
    }

    private void OnSelectClick(object? sender, EventArgs e)
    {
        if (_selectedImage != null)
        {
            ImageSelected?.Invoke(this, _selectedImage);
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    /// <summary>
    /// Gets the selected image path.
    /// </summary>
    public string? SelectedImagePath => _selectedImage;
}
