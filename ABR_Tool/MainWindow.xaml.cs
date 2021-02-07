using Microsoft.Win32;
using Pfim;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace ABR_Tool
{
    /// <summary>
    /// The structure of a Texture File Entry. A combination between the entry in the header,
    /// the actual texture, and the file name taken from the list at the end of the archive
    /// </summary>
    public class TextureFileEntry
    {
        public int texture_index { get; set; } // The index of the file. Not actually included in the file, just added for making it easier to handle these.
        public int file_size { get; set; } // The total size of the texture file
        public int unknown1 { get; set; } // Unknown. Perhaps parameters
        public int unknown2 { get; set; } // This seems to be some kind of offset? Weirdly changes between files, and doesn't make any sense
        public int next_offset { get; set; } // Offset of the next file in the archive
        public byte[] texture_file { get; set; } // The actual DDS texture
        public string file_name { get; set; } // The filename of the texture
        public BitmapSource texture_bitmap { get; set; } // The texture, but converted to a wpf bitmapsource for preview purposes
        public string surface_type { get; set; } // Store DDS surface type to warn for incompatability issues when reimporting 
        public uint mipmap_count { get; set; }
        public long stream_position { get; set; }
    }

    public class DDSEntry
    {
        public int texture_index { get; set; } // The index of the file. Not actually included in the file, just added for making it easier to handle these.
        public long texture_offset { get; set; } // Offset / Position of the file in the entire file stream
        public int file_size { get; set; } // The total size of the texture file
        public byte[] texture_file { get; set; } // The entire DDS texture as a byte array
        public string file_name { get; set; } // The name of the dds file
        public BitmapSource texture_bitmap { get; set; } // The dds converted to a bitmap for preview purposes
        public string surface_type { get; set; } // Store DDS surface type to warn for incompatability issues when reimporting 
        public uint mipmap_count { get; set; }

    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // General header variables, and things we need to store for saving again later.
        // At least until we can figure out how to reconstruct on the fly
        byte[] magic = new byte[4];
        int amount_of_files = 0;
        int total_size_of_files = 0;
        int header_size = 0;
        byte[] unknown_header = new byte[8];
        byte[] header_padding = new byte[8];

        // Total file size, needed for proper memstream size or we'll run out of memory later. Woops...
        long total_archive_size = 0;

        // might wanna save some positions in the file stream
        long position_start_first_file = 0;
        long position_file_name_list = 0;

        string current_file_path = "";
        string original_file_name = "";

        ObservableCollection<DDSEntry> all_dds_files = new ObservableCollection<DDSEntry>();
        List<long> foundDDSPositions = new List<long>();

        List<string> all_file_names = new List<string>();

        string lastUsedABRPath = Properties.Settings.Default.lastABRPath;
        string lastUsedDDSPath = Properties.Settings.Default.lastDDSPath;

        private static List<GCHandle> handles = new List<GCHandle>();

        public MainWindow()
        {
            InitializeComponent();
        }

        private void LoadABRFile(object sender, RoutedEventArgs e)
        {

            for (int i = 0; i < all_dds_files.Count(); i++)
            {
                all_dds_files[i] = null;
            }

            all_dds_files.Clear();
            foundDDSPositions.Clear();

            foreach (var handle in handles)
            {
               // handle.Free();
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Texture Archive (*.abr, *.efo, *.kmr)|*.abr; *.efo; *.kmr";
            openFileDialog.InitialDirectory = lastUsedABRPath;
            if (openFileDialog.ShowDialog() == true)
            {
                using (FileStream fs = File.OpenRead(openFileDialog.FileName))
                {
                    current_file_path = openFileDialog.FileName;
                    original_file_name = System.IO.Path.GetFileName(openFileDialog.FileName);
                    this.Title = "ABR Tool | " + original_file_name;
                    total_archive_size = new System.IO.FileInfo(current_file_path).Length;

                    lastUsedABRPath = System.IO.Path.GetDirectoryName(openFileDialog.FileName);
                    Properties.Settings.Default.lastABRPath = lastUsedABRPath;
                    Properties.Settings.Default.Save();

                    long bufferSize = Math.Min(fs.Length, 100_000);
                    byte[] buffer = new byte[bufferSize];

                    //List<long> searchResults = new List<long>();
                    long filePosition = 0;
                    //uint pattern = 0x00010203; // Big Endian
                    uint ddsPattern = 0x44445320;
                    uint view = 0; // bytes shifted into this for easy compare
                    int readCount = 0;

                    while ((readCount = fs.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        for (int i = 0; i < readCount; i++) // see Henrik's answer
                        {
                            view = (view << 8) | buffer[i]; // shift-in next byte
                            if (view == ddsPattern && filePosition >= 3) // make sure we already got at least 4 bytes
                                foundDDSPositions.Add(filePosition - 3);
                            filePosition++;
                        }
                    }
                    Console.WriteLine("Number of textures found: " + foundDDSPositions.Count());

                    int current_index = 0;

                    foreach (long offset in foundDDSPositions)
                    {
                        int foundTextureSize = 0;
                        fs.Position = offset - 4;
                        byte[] sizeBuffer = new byte[4];
                        fs.Read(sizeBuffer, 0, 4);
                        foundTextureSize = BitConverter.ToInt32(sizeBuffer, 0);
                        //fs.Position = offset;
                        byte[] headerBuffer = new byte[128];
                        fs.Read(headerBuffer, 0, 128);
                        Stream bufferStream = new MemoryStream(headerBuffer);

                        //foundTextureSize = GetTextureSize(bufferStream);
                        Console.WriteLine("Found a texture with size: " + foundTextureSize);

                        DDSEntry foundEntry = new DDSEntry();
                        foundEntry.file_size = foundTextureSize;

                        byte[] fileBuffer = new byte[foundTextureSize];
                        fs.Position = offset;
                        fs.Read(fileBuffer, 0, (int)foundEntry.file_size);
                        foundEntry.texture_file = new byte[foundEntry.file_size];
                        foundEntry.texture_offset = offset;
                        fileBuffer.CopyTo(foundEntry.texture_file, 0);

                        // File name parsing. This is a bit of a hack....
                        byte terminator = 0x00;
                        byte[] char_buffer = new byte[1];
                        fs.Position += 7;
                        fs.Read(char_buffer, 0, 1);
                        string name_buffer = "";
                        for (int buffer_index = 0; buffer_index < 10000; buffer_index++)
                        {
                            if (!char_buffer[0].Equals(terminator))
                            {

                                //Console.WriteLine("Reading char");
                                name_buffer += System.Text.Encoding.UTF8.GetString(char_buffer);
                                fs.Read(char_buffer, 0, 1);
                            }
                            else
                            {
                                buffer_index = 20000;
                                //Console.WriteLine("Reading STOPPED");
                            }
                        }

                        foundEntry.file_name = name_buffer;

                        // Convert texture file to a bitmap source for preview purposes
                        Stream image_stream = new MemoryStream(foundEntry.texture_file);
                        IImage image = Pfim.Pfim.FromStream(image_stream);
                        foundEntry.texture_bitmap = WpfImageSource(image);
                        image_stream.Position = 0;
                        foundEntry.surface_type = GetSurfaceType(image_stream);
                        image_stream.Position = 0;
                        try
                        {
                            foundEntry.mipmap_count = GetMipMapCount(image_stream);
                        }
                        catch
                        {
                            foundEntry.mipmap_count = 0;
                        }

                        foundEntry.texture_index = current_index;
                        current_index++;
                        //HandleTextureBytes(file_index);

                        all_dds_files.Add(foundEntry);
                    }

                    MainGrid.UpdateLayout();
                    TextureListBox.ItemsSource = all_dds_files;
                    MySnackbar.MessageQueue.Enqueue("Loaded archive " + original_file_name);
                }
            }
            else
            {
                MySnackbar.MessageQueue.Enqueue("No archive loaded.");
                this.Title = "ABR Tool";
            }

        }
   
        private void SaveArchiveFile(object sender, RoutedEventArgs e)
        {
            
            int archive_size_integer = (int)total_archive_size;
            Stream new_stream = new MemoryStream(archive_size_integer);
            // Load original  archive into memory
            using (Stream stream = File.OpenRead(current_file_path))
            {
                stream.CopyTo(new_stream);
            }

            // Replace each texture in .pac with the one currently in memory. Kinda inefficient but whatever. 
            // This code does not need to win any awards
            for (int i = 0; i < all_dds_files.Count; i++)
            {
                new_stream.Position = all_dds_files[i].texture_offset;
                new_stream.Write(all_dds_files[i].texture_file, 0, all_dds_files[i].texture_file.Length);
            }

            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.FileName = original_file_name;
            saveFileDialog.AddExtension = true;
            saveFileDialog.InitialDirectory = lastUsedABRPath;

            if (saveFileDialog.ShowDialog() == true)
            {
                lastUsedABRPath = System.IO.Path.GetDirectoryName(saveFileDialog.FileName);
                Properties.Settings.Default.lastABRPath = lastUsedABRPath;
                Properties.Settings.Default.Save();
                using (FileStream outputFileStream = new FileStream(saveFileDialog.FileName, FileMode.Create))
                {
                    new_stream.Position = 0;
                    new_stream.CopyTo(outputFileStream);
                }
                MySnackbar.MessageQueue.Enqueue("Saved archive");
            }
        }

        private static PixelFormat PixelFormat(IImage image)
        {
            switch (image.Format)
            {
                case ImageFormat.Rgba16:
                    return PixelFormats.Bgr555;
                // This is wrong, it's technically BGRA4444 but c# does not support that, so... rip
                case ImageFormat.Rgb24:
                    return PixelFormats.Bgr24;
                case ImageFormat.Rgba32:
                    return PixelFormats.Bgra32;
                case ImageFormat.Rgb8:
                    return PixelFormats.Gray8;
                case ImageFormat.R5g5b5a1:
                case ImageFormat.R5g5b5:
                    return PixelFormats.Bgr555;
                case ImageFormat.R5g6b5:
                    return PixelFormats.Bgr565;
                default:
                    throw new Exception($"Unable to convert {image.Format} to WPF PixelFormat");
            }
        }

        private static BitmapSource WpfImageSource(IImage image)
        {
            var pinnedArray = GCHandle.Alloc(image.Data, GCHandleType.Pinned);
            //handles.Add(pinnedArray);
            var addr = pinnedArray.AddrOfPinnedObject();
            var NewBitmap = BitmapSource.Create(image.Width, image.Height, 96.0, 96.0,
                PixelFormat(image), null, addr, image.DataLen, image.Stride);
            pinnedArray.Free();
            return NewBitmap;
        }

        private static IEnumerable<Image> WpfImage(IImage image)
        {
            var pinnedArray = GCHandle.Alloc(image.Data, GCHandleType.Pinned);
            var addr = pinnedArray.AddrOfPinnedObject();
            var bsource = BitmapSource.Create(image.Width, image.Height, 96.0, 96.0,
                PixelFormat(image), null, addr, image.DataLen, image.Stride);

            handles.Add(pinnedArray);
            yield return new Image
            {
                Source = bsource,
                Width = image.Width,
                Height = image.Height,
                MaxHeight = image.Height,
                MaxWidth = image.Width,
                Margin = new Thickness(4)
            };

            foreach (var mip in image.MipMaps)
            {
                var mipAddr = addr + mip.DataOffset;
                var mipSource = BitmapSource.Create(mip.Width, mip.Height, 96.0, 96.0,
                    PixelFormat(image), null, mipAddr, mip.DataLen, mip.Stride);
                yield return new Image
                {
                    Source = mipSource,
                    Width = mip.Width,
                    Height = mip.Height,
                    MaxHeight = mip.Height,
                    MaxWidth = mip.Width,
                    Margin = new Thickness(4)
                };
            }
        }

        private void ExportDDS(object sender, RoutedEventArgs e)
        {
            int texture_index = (int)((Button)sender).Tag;
            Console.WriteLine("Trying to export texture id " + texture_index.ToString());

            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.FileName = all_dds_files[texture_index].file_name;
            saveFileDialog.Filter = "DDS Texture (*.dds)|*.dds";
            saveFileDialog.DefaultExt = "dds";
            saveFileDialog.AddExtension = false;
            saveFileDialog.InitialDirectory = lastUsedDDSPath;
            if (saveFileDialog.ShowDialog() == true)
            {
                lastUsedDDSPath = System.IO.Path.GetDirectoryName(saveFileDialog.FileName);
                Properties.Settings.Default.lastDDSPath = lastUsedDDSPath;
                Properties.Settings.Default.Save();
                File.WriteAllBytes(saveFileDialog.FileName, all_dds_files[texture_index].texture_file);
                MySnackbar.MessageQueue.Enqueue("Exported texture successfully.");
            }

        }

        private void ImportDDS(object sender, RoutedEventArgs e)
        {
            int texture_index = (int)((Button)sender).Tag;
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.FileName = all_dds_files[texture_index].file_name;
            openFileDialog.Filter = "DDS Texture (*.dds)|*.dds";
            openFileDialog.DefaultExt = "dds";
            openFileDialog.InitialDirectory = lastUsedDDSPath;
            if(openFileDialog.ShowDialog() == true)
            {
                lastUsedDDSPath = System.IO.Path.GetDirectoryName(openFileDialog.FileName);
                Properties.Settings.Default.lastDDSPath = lastUsedDDSPath;
                Properties.Settings.Default.Save();

                long length = File.ReadAllBytes(openFileDialog.FileName).Length;

                if (length != all_dds_files[texture_index].file_size)
                {
                    using (FileStream fs = File.OpenRead(openFileDialog.FileName))
                    {
                        string imported_type = "";
                        uint imported_mips = 0;
                        fs.Position = 0;
                        imported_type = GetSurfaceType(fs);
                        fs.Position = 0;
                        imported_mips = GetMipMapCount(fs);

                        // Check if the surface type matches, and warn the user if it doesn't
                        if (imported_type == all_dds_files[texture_index].surface_type)
                        {
                            Console.WriteLine("Surface type matches. Importing.");
                        }
                        else
                        {
                            MessageBoxResult result = MessageBox.Show(Application.Current.MainWindow, "Surface Type does not match original texture. \nThis might lead to wrong colors or other glitches ingame. \nTry importing anyway?\n\nOriginal Type: " + ShortDDSType(all_dds_files[texture_index].surface_type) + "\nNew Type: " + ShortDDSType(imported_type), "Import Warning", MessageBoxButton.YesNo);
                            switch (result)
                            {
                                case MessageBoxResult.Yes:
                                    break;
                                case MessageBoxResult.No:
                                    return;
                            }
                        }

                        // Check if the amount of mipmaps is correct
                        if (imported_mips == all_dds_files[texture_index].mipmap_count)
                        {
                            Console.WriteLine("Correct amount of mipmaps. Importing.");
                        }
                        else
                        {
                            MessageBoxResult result = MessageBox.Show(Application.Current.MainWindow, "The amount of mipmaps does not match with the original texture. \nTry importing anyway?\n\nOriginal Mipmap count: " + all_dds_files[texture_index].mipmap_count.ToString() + "\nNew count: " + imported_mips.ToString(), "Import Warning", MessageBoxButton.YesNo);
                            switch (result)
                            {
                                case MessageBoxResult.Yes:
                                    break;
                                case MessageBoxResult.No:
                                    return;
                            }
                        }

                        MessageBox.Show(Application.Current.MainWindow, "New texture file size is smaller or bigger than the original. \nThis is currently not supported. \nMake sure you are using the right surface type and amount of mip maps and also correct height and width. \nCancelling import.", "Import Error");
                        return;
                    } 

                }

                using (FileStream fs = File.OpenRead(openFileDialog.FileName))
                {
                    string imported_type = "";
                    uint imported_mips = 0;
                    fs.Position = 0;
                    imported_type = GetSurfaceType(fs);
                    fs.Position = 0;
                    imported_mips = GetMipMapCount(fs);

                    // Check if the surface type matches, and warn the user if it doesn't
                    if (imported_type == all_dds_files[texture_index].surface_type)
                    {
                        Console.WriteLine("Surface type matches. Importing.");
                    } 
                    else
                    {
                        MessageBoxResult result = MessageBox.Show(Application.Current.MainWindow, "Surface Type does not match original texture. \nThis might lead to wrong colors or other glitches ingame. \nTry importing anyway?\n\nOriginal Type: " + ShortDDSType(all_dds_files[texture_index].surface_type) + "\nNew Type: " + ShortDDSType(imported_type), "Import Warning", MessageBoxButton.YesNo);
                        switch (result)
                        {
                            case MessageBoxResult.Yes:
                                break;
                            case MessageBoxResult.No:
                                return;
                        }
                    }

                    // Check if the amount of mipmaps is correct
                    if (imported_mips == all_dds_files[texture_index].mipmap_count)
                    {
                        Console.WriteLine("Correct amount of mipmaps. Importing.");
                    }
                    else
                    {
                        MessageBoxResult result = MessageBox.Show(Application.Current.MainWindow, "The amount of mipmaps does not match with the original texture. \nTry importing anyway?\n\nOriginal Mipmap count: " + all_dds_files[texture_index].mipmap_count.ToString() + "\nNew count: " + imported_mips.ToString(), "Import Warning", MessageBoxButton.YesNo);
                        switch (result)
                        {
                            case MessageBoxResult.Yes:
                                break;
                            case MessageBoxResult.No:
                                return;
                        }
                    }

                    all_dds_files[texture_index].texture_file = File.ReadAllBytes(openFileDialog.FileName);
                    HandleTextureBytes(texture_index);
                }

                MySnackbar.MessageQueue.Enqueue("Imported texture successfully.");
                // Refresh Texture List
                TextureListBox.ItemsSource = null;
                MainGrid.UpdateLayout();
                TextureListBox.ItemsSource = all_dds_files;
                MainGrid.UpdateLayout();
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

       private string GetSurfaceType(Stream texture_fs)
        {
            DdsHeader header = new DdsHeader(texture_fs);
            string found_type = header.PixelFormat.FourCC.ToString();
            Console.WriteLine(header.PixelFormat.FourCC.ToString());
            if (header.PixelFormat.FourCC.ToString() == "DX10")
            {
                var header10 = new DdsHeaderDxt10(texture_fs);
                found_type = header10.DxgiFormat.ToString();
            } else
            {
                if(header.PixelFormat.FourCC.ToString() == "None")
                {
                    DdsPixelFormat fourCC = header.PixelFormat;
                    found_type = "Uncompressed RGB" + fourCC.RGBBitCount.ToString();
                }
            }
            return found_type;
        }

        private uint GetMipMapCount(Stream texture_fs)
        {
            DdsHeader header = new DdsHeader(texture_fs);
            uint found_count = header.MipMapCount;
            return found_count;
        }

        private void HandleTextureBytes(int file_index)
        {
            // Convert texture file to a bitmap source for preview purposes
            Stream image_stream = new MemoryStream(all_dds_files[file_index].texture_file);
            IImage image = Pfim.Pfim.FromStream(image_stream);
            all_dds_files[file_index].texture_bitmap = WpfImageSource(image);
            image_stream.Position = 0;
            all_dds_files[file_index].surface_type = GetSurfaceType(image_stream);
        }

        private BitmapSource HandleTextureBytes(IImage image)
        {
            // Convert texture file to a bitmap source for preview purposes
            return WpfImageSource(image);
        }

        public string ShortDDSType(string original)
        {
            //float convertedValue = (int)value / 1024;
            //return System.Convert.ToInt32(convertedValue).ToString() + " KB";
            string newFormat = (string)original;
            return newFormat.Replace("D3DFMT_", "");
        }

        private void ExportAllTextures(object sender, RoutedEventArgs e)
        {
            if(all_dds_files.Count > 0)
            {
                SaveFileDialog saveFileDialog = new SaveFileDialog();
                saveFileDialog.FileName = all_dds_files[0].file_name;
                saveFileDialog.Filter = "DDS Texture (*.dds)|*.dds";
                saveFileDialog.DefaultExt = "dds";
                saveFileDialog.AddExtension = false;
                saveFileDialog.InitialDirectory = lastUsedDDSPath;
                if (saveFileDialog.ShowDialog() == true)
                {
                    string exportFolder = System.IO.Path.GetDirectoryName(saveFileDialog.FileName);
                    lastUsedDDSPath = exportFolder;
                    Properties.Settings.Default.lastDDSPath = lastUsedDDSPath;
                    Properties.Settings.Default.Save();
                    for (int i = 0; i < all_dds_files.Count; i++)
                    {

                        Console.WriteLine("Exporting number " + i.ToString());
                        string currentFileName = System.IO.Path.GetDirectoryName(saveFileDialog.FileName);
                        currentFileName = currentFileName + "/" + all_dds_files[i].file_name;
                        File.WriteAllBytes(currentFileName, all_dds_files[i].texture_file);
                    }
                    MessageBox.Show("Exported all textures");
                }
            } 
            else
            {
                MessageBox.Show("No textures found. Load a valid archive first.");
            }

        }

        private void ImportAllTextures(object sender, RoutedEventArgs e)
        {
            if (all_dds_files.Count > 0)
            {
                OpenFileDialog openFileDialog = new OpenFileDialog();
                openFileDialog.FileName = all_dds_files[0].file_name;
                openFileDialog.Filter = "DDS Texture (*.dds)|*.dds";
                openFileDialog.DefaultExt = "dds";
                openFileDialog.InitialDirectory = lastUsedDDSPath;
                if (openFileDialog.ShowDialog() == true)
                {
                    string importFolder = System.IO.Path.GetDirectoryName(openFileDialog.FileName);
                    lastUsedDDSPath = importFolder;
                    Properties.Settings.Default.lastDDSPath = lastUsedDDSPath;
                    Properties.Settings.Default.Save();

                    for (int texture_index = 0; texture_index < all_dds_files.Count; texture_index++)
                    {
                        string currentTexturePath = importFolder + "/" + all_dds_files[texture_index].file_name;
                        Console.WriteLine("Trying to import " + all_dds_files[texture_index].file_name);

                        if(File.Exists(currentTexturePath))
                        {
                            Console.WriteLine("File found: " + currentTexturePath);
                            long length = File.ReadAllBytes(currentTexturePath).Length;

                            if (length != all_dds_files[texture_index].file_size)
                            {
                                MessageBox.Show("New texture file size is smaller or bigger than the original. This is currently not supported. Cancelling import.", "Import Error");
                                return;
                            }

                            using (FileStream fs = File.OpenRead(currentTexturePath))
                            {
                                string imported_type = "";
                                imported_type = GetSurfaceType(fs);

                                // Check if the surface type matches, and warn the user if it doesn't
                                if (imported_type == all_dds_files[texture_index].surface_type)
                                {
                                    Console.WriteLine("Surface type matches. Importing.");
                                }
                                else
                                {
                                    MessageBoxResult result = MessageBox.Show("Surface Type does not match original texture. This might lead to wrong colors or other glitches ingame. Import anyway?", "Import Warning", MessageBoxButton.YesNo);
                                    switch (result)
                                    {
                                        case MessageBoxResult.Yes:
                                            break;
                                        case MessageBoxResult.No:
                                            return;
                                    }
                                }

                                all_dds_files[texture_index].texture_file = File.ReadAllBytes(currentTexturePath);
                                HandleTextureBytes(texture_index);

                                //long length = new File.ReadAllBytes(path).Length;

                            }

                            // Refresh Texture List
                            TextureListBox.ItemsSource = null;
                            TextureListBox.ItemsSource = all_dds_files;
                            GC.Collect();
                            GC.WaitForPendingFinalizers();
                        } else
                        {
                            Console.WriteLine("File NOT found: " + currentTexturePath);
                        }
                    }
                }
            }
        }
    }


    public class ByteToKilobyteConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            float convertedValue = (int)value / 1024;
            return System.Convert.ToInt32(convertedValue).ToString() + " KB";
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            int convertedValue = (int)value * 1014;
            return convertedValue;
        }
    }

    public class SurfaceTypeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            //float convertedValue = (int)value / 1024;
            //return System.Convert.ToInt32(convertedValue).ToString() + " KB";
            string newFormat = (string)value;
            return newFormat.Replace("D3DFMT_", "");
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return value;
        }
    }

}

