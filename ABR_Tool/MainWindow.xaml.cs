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

        //ObservableCollection<TextureFileEntry> all_old_filesD = new ObservableCollection<TextureFileEntry>();

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

        public void UpdateProgress(int progress)
        {
            Dispatcher.Invoke((Action)(() =>
            {
                Generic_ProgressBar.Value = progress;
            }));

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
                using (FileStream fs = File.OpenRead(openFileDialog.FileName))
                {
                    current_file_path = openFileDialog.FileName;
                    original_file_name = System.IO.Path.GetFileName(openFileDialog.FileName);
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

                    foreach(long offset in foundDDSPositions)
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
                        } catch
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
                }



        }
        #region oldLoadCode
        private void LoadPacFile(object sender, RoutedEventArgs e)
        {
           /*
            foreach (var handle in handles)
            {
                handle.Free();
            }

            all_dds_files.Clear();
            position_file_name_list = 0;
            position_start_first_file = 0;
            all_file_names.Clear();
            Generic_ProgressBar.Value = 0;

            GC.Collect();
            GC.WaitForPendingFinalizers();

            OpenFileDialog openFileDialog = new OpenFileDialog();
            if (openFileDialog.ShowDialog() == true)
                using (FileStream fs = File.OpenRead(openFileDialog.FileName))
                {
                    current_file_path = openFileDialog.FileName;
                    original_file_name = System.IO.Path.GetFileName(openFileDialog.FileName);
                    total_archive_size = new System.IO.FileInfo(current_file_path).Length;

                    Thread thread = new Thread(delegate ()
                    {
                        UpdateProgress(0);
                    });
                    thread.IsBackground = false;
                    thread.Start();

                    byte[] b = new byte[4];
                    UTF8Encoding temp = new UTF8Encoding(true);

                    fs.Read(b, 0, 4); // Get Magic
                    Console.WriteLine("Magic: " + temp.GetString(b));
                    Generic_ProgressBar.Value = 10;
                    if (temp.GetString(b) != "pack")
                    {
                        MessageBox.Show("Error: Not a valid pac file.");
                        return;
                    }

                    int current_texture_index = 0;
                    ((MainWindow)System.Windows.Application.Current.MainWindow).UpdateLayout();
                    b.CopyTo(magic, 0);
                    fs.Read(b, 0, 4); // Get amount of files
                    amount_of_files = BitConverter.ToInt32(b, 0);
                    Console.WriteLine("Number of texture files: " + amount_of_files.ToString());
                    fs.Read(b, 0, 4); // Get total size of all files
                    total_size_of_files = BitConverter.ToInt32(b, 0);
                    Console.WriteLine("Total filesize of all textures: " + total_size_of_files.ToString());
                    fs.Read(b, 0, 4); // Get total size of all files
                    header_size = BitConverter.ToInt32(b, 0);
                    Console.WriteLine("Header size: " + header_size.ToString());
                    fs.Read(unknown_header, 0, 8); // Read the unknown header bits.
                    fs.Read(header_padding, 0, 8); // Header padding
                    thread = new Thread(delegate ()
                    {
                        UpdateProgress(10);
                    });

                    int progressbar_per_file = 0;
                    progressbar_per_file = 80 / amount_of_files;

                    // iterate through the file headers
                    for (int i = 0; i < amount_of_files; i++)
                    {
                        TextureFileEntry current_file_entry = new TextureFileEntry();
                        fs.Read(b, 0, 4); // Get File_size
                        current_file_entry.file_size = BitConverter.ToInt32(b, 0);
                        fs.Read(b, 0, 4); // Get Unknown 1
                        current_file_entry.unknown1 = BitConverter.ToInt32(b, 0);
                        fs.Read(b, 0, 4); // Get Unknown 2
                        current_file_entry.unknown2 = BitConverter.ToInt32(b, 0);
                        fs.Read(b, 0, 4); // Get next_offset
                        current_file_entry.next_offset = BitConverter.ToInt32(b, 0);

                        current_file_entry.texture_index = current_texture_index;
                        current_texture_index++;

                        //all_dds_files.Add(current_file_entry);
                        
                        int progress = (int)Generic_ProgressBar.Value + progressbar_per_file;
                        thread = new Thread(delegate ()
                        {
                            UpdateProgress(progress);
                        });
                    }
                    
                    if (amount_of_files % 2 == 1)
                    {
                        // archives where the file amount can't be evenly split in half get 16 bytes of padding added to the end of the header 
                        byte[] weird_padding = new byte[16];
                        fs.Read(weird_padding, 0, 16);
                    }

                    position_start_first_file = fs.Position;

                    // get the actual files
                    for (int file_index = 0; file_index < amount_of_files; file_index++)
                    {
                        // Read the texture into memory
                        Console.WriteLine("Trying to allocate " + all_dds_files[file_index].file_size.ToString());
                        byte[] texture_bytes = new byte[all_dds_files[file_index].file_size];
                        all_dds_files[file_index].stream_position = fs.Position;
                        fs.Read(texture_bytes, 0, texture_bytes.Length);
                        all_dds_files[file_index].texture_file = new byte[all_dds_files[file_index].file_size];
                        texture_bytes.CopyTo(all_dds_files[file_index].texture_file, 0);
                        Console.WriteLine("Texture is " + all_dds_files[file_index].texture_file.Length.ToString() + " bytes long");
                        if (all_dds_files[file_index].next_offset != 0)
                        {
                            fs.Position = all_dds_files[file_index].next_offset + position_start_first_file;
                        }
                        else
                        {

                        }
                        // Convert texture file to a bitmap source for preview purposes
                        /*Stream image_stream = new MemoryStream(all_dds_files[file_index].texture_file);
                        IImage image = Pfim.Pfim.FromStream(image_stream);
                        all_dds_files[file_index].texture_bitmap = WpfImageSource(image);
                        image_stream.Position = 0;
                        all_dds_files[file_index].surface_type = GetSurfaceType(image_stream);
                        */
                        /*
                        HandleTextureBytes(file_index);

                    }
                    Generic_ProgressBar.Value = 90;
                    ((MainWindow)System.Windows.Application.Current.MainWindow).UpdateLayout();
                    // Go to position of file name list
                    position_file_name_list = position_start_first_file + total_size_of_files;
                    fs.Position = position_file_name_list;

                    // Read the entire file name list (so till end of file) into one stream, and split afterwards
                    long size_of_rest_of_file = fs.Length - fs.Position;
                    byte terminator = 0x00;
                    byte[] name_list_buffer = new byte[size_of_rest_of_file];
                    for (int file_name_index = 0; file_name_index < amount_of_files; file_name_index++)
                    {
                        byte[] char_buffer = new byte[1];
                        fs.Read(char_buffer, 0, 1);
                        string name_buffer = "";
                        for (int buffer_index = 0; buffer_index < 10000; buffer_index++)
                        {
                            if(!char_buffer[0].Equals(terminator))
                            {
                                
                                Console.WriteLine("Reading char");
                                name_buffer += System.Text.Encoding.UTF8.GetString(char_buffer);
                                fs.Read(char_buffer, 0, 1);
                            }
                            else
                            {
                                buffer_index = 20000;
                                Console.WriteLine("Reading STOPPED");
                            }
                        }

                        all_dds_files[file_name_index].file_name = name_buffer;
                        Console.WriteLine("Found texture name buffer: " + name_buffer);
                        //name_buffer = "";
                    }

                    for(int i = 0; i < amount_of_files; i++)
                    {
                        Console.WriteLine("Found texture: " + all_dds_files[i].file_name);
                    }
                    Generic_ProgressBar.Value = 100;
                    MainGrid.UpdateLayout();
                    TextureListBox.ItemsSource = all_dds_files;
                }
        */
                    }
        #endregion
        private void SaveArchiveFile(object sender, RoutedEventArgs e)
        {
            
            int archive_size_integer = (int)total_archive_size;
            Stream new_stream = new MemoryStream(archive_size_integer);
            //Stream pac_file_stream = new MemoryStream(File.ReadAllBytes(current_file_path));
            // Load original .pac archive into memory
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
                    MessageBox.Show("New texture file size is smaller or bigger than the original.This is currently not supported. Cancelling import. Make sure you are using the right surface type and amount of mip maps.", "Import Error");
                    return;
                }

                using (FileStream fs = File.OpenRead(openFileDialog.FileName))
                {
                    string imported_type = "";
                    uint imported_mips = 0;
                    imported_type = GetSurfaceType(fs);
                    imported_mips = GetMipMapCount(fs);

                    // Check if the surface type matches, and warn the user if it doesn't
                    if (imported_type == all_dds_files[texture_index].surface_type)
                    {
                        Console.WriteLine("Surface type matches. Importing.");
                    } 
                    else
                    {
                        MessageBoxResult result = MessageBox.Show("Surface Type does not match original texture. This might lead to wrong colors or other glitches ingame. Import anyway?", "Import Warning", MessageBoxButton.YesNo);
                        switch(result)
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
                        MessageBoxResult result = MessageBox.Show("The amount of mipmaps does not match with the original texture. ", "Import Warning", MessageBoxButton.YesNo);
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

                    //long length = new File.ReadAllBytes(path).Length;

                }

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
            //Console.WriteLine(header.PixelFormat.FourCC.ToString());
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
            //.WriteLine("Found Alpha bitmask: " + header.PixelFormat.ABitMask + "\n");
            return found_count;
        }

        [Obsolete("This does not actually get used, as the archive has the file size directly in front of the texture.")]
        private int GetTextureSize(Stream texture_fs)
        {
            DdsHeader header = new DdsHeader(texture_fs);
            string found_type = header.PixelFormat.FourCC.ToString();
            Console.WriteLine("Found texture type: " + found_type);
            int texture_size = 0;

            texture_size = (int)header.PitchOrLinearSize + (int)header.Size + 11;
            Console.WriteLine("Found a texture with size before mip maps:" + texture_size);
            int current_height = (int)header.Height;
            int current_width = (int)header.Width;

            var len = (int)header.PitchOrLinearSize;
            var totalLen = 0;

            if (header.MipMapCount > 0)
            {
                for (int i = 1; i < header.MipMapCount; i++)
                {
                    current_height = current_height / 2;
                    current_width = current_width / 2;

                   int mipmap_size = current_height * current_width * 1;

                    if (found_type == "D3DFMT_DXT5" || found_type == "D3DFMT_DXT3")
                        //mipmap_size = mipmap_size / 2;
                        mipmap_size = (current_height * current_width) * 4;

                    if (found_type == "D3DFMT_DXT1")
                        //mipmap_size = mipmap_size / 4;
                        mipmap_size = (current_height * current_width) * 2;

                    texture_size += mipmap_size;

                }
            }
            return texture_size;
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
                        string currentTexturePath = importFolder + "/" + all_dds_files[texture_index].file_name + ".dds";
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

}

