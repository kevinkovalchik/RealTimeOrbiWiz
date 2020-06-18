using System;
using System.IO;
using System.Security.Permissions;
using System.Threading.Tasks;
using ThermoFisher.CommonCore.Data.Business;
using ThermoFisher.CommonCore.Data;
using System.Linq;

namespace RealTimeOrbiWiz
{
    class Program
    {
        public static void Main(string[] args)
        {
            Run(args);
        }

        [PermissionSet(SecurityAction.Demand, Name = "FullTrust")] // I don't remember why this is here. I think something to do with watching the filesystem, but maybe try deleting it and see if things still work.
        private static void Run(string[] args)
        {

            // If a directory is not specified, exit program.
            if (args.Length != 1)
            {
                // Display the proper way to call the program.
                Console.WriteLine("Usage: RealTimeOrbiWiz.exe (directory)");
                return;
            }

            // Create a new FileSystemWatcher and set its properties.
            using (FileSystemWatcher watcher = new FileSystemWatcher())
            {
                watcher.Path = args[0];

                // Watch for changes in LastAccess and LastWrite times, and
                // the renaming of files or directories.
                watcher.NotifyFilter = NotifyFilters.FileName
                                     | NotifyFilters.CreationTime;

                // Only watch .raw files.
                watcher.Filter = "*.raw";

                // Add event handlers.
                //watcher.Changed += OnChanged;
                watcher.Created += OnCreated;
                //watcher.Deleted += OnChanged;
                //watcher.Renamed += OnRenamed;

                // Begin watching.
                watcher.EnableRaisingEvents = true;

                // Wait for the user to quit the program.
                // Console.WriteLine("Press 'q' to quit the sample.");
                Console.WriteLine("Running. Waiting for new files to show up... Press 'q' to quit");
                while (Console.Read() != 'q') { };
            }
        }

        // Define the event handlers.
        private static void OnChanged(object source, FileSystemEventArgs e) =>
            // Specify what is done when a file is changed, created, or deleted.
            Console.WriteLine($"File: {e.FullPath} {e.ChangeType}");

        private static void OnRenamed(object source, RenamedEventArgs e) =>
            // Specify what is done when a file is renamed.
            Console.WriteLine($"File: {e.OldFullPath} renamed to {e.FullPath}");

        private static void OnCreated(object source, FileSystemEventArgs e)
        {
            Console.WriteLine($"File: {e.FullPath} created");
            Task.Run(() => WatchFile(e.FullPath)).Wait();
        }
        
        private static void WatchFile(string pathToRawFile)
        {
            using (var f = new StreamWriter("rawFileAcquisitionInfo.txt", append: true))
            {
                using (var rawFile = RawFileReaderFactory.CreateThreadManager(pathToRawFile).CreateThreadAccessor())
                {
                    rawFile.SelectMsData();

                    DateTime created = DateTime.Now;

                    f.WriteLine($"{pathToRawFile} created at {created}");
                    f.WriteLine($"InAcquisition: {rawFile.InAcquisition}");
                    f.WriteLine($"Run header: {rawFile.RunHeader.ToString()}");
                    f.WriteLine($"Spectra count: {rawFile.RunHeaderEx.SpectraCount}");

                    Console.WriteLine($"{pathToRawFile} created at {created}");
                    Console.WriteLine($"InAcquisition: {rawFile.InAcquisition}");
                    Console.WriteLine($"Run header: {rawFile.RunHeader.ToString()}");
                    Console.WriteLine($"Spectra count: {rawFile.RunHeaderEx.SpectraCount}");

                    int lastSpectrum = rawFile.RunHeader.LastSpectrum;
                    int spectraCount = rawFile.RunHeaderEx.SpectraCount;

                    while (spectraCount == rawFile.RunHeaderEx.SpectraCount || lastSpectrum == rawFile.RunHeader.LastSpectrum)
                    {
                        Console.WriteLine($"Waiting for a spectrum to be recorded. {(DateTime.Now - created).TotalSeconds} seconds elapsed.");
                        f.WriteLine($"Waiting for a spectrum to be recorded. {(DateTime.Now - created).TotalSeconds} seconds elapsed.");
                        Task.Delay(5000).Wait();
                        rawFile.RefreshViewOfFile();
                    }

                    Console.WriteLine("A spectrum has been recorded.");
                    f.WriteLine("A spectrum has been recorded.");

                    var firstRecordedSpec = rawFile.RunHeader.FirstSpectrum;
                    var rtInSecBeforeFirstScan = rawFile.RetentionTimeFromScanNumber(firstRecordedSpec) * 60;

                    var deadTime = (DateTime.Now - created).TotalSeconds - rtInSecBeforeFirstScan;
                    Console.WriteLine($"Dead time: {deadTime}");
                    f.WriteLine($"Dead time: {deadTime}");

                    Console.WriteLine("Now watching the file as new spectra are recorded.");
                    f.WriteLine("Now watching the file as new spectra are recorded.");

                    while (rawFile.InAcquisition)
                    {
                        rawFile.RefreshViewOfFile();

                        lastSpectrum = rawFile.RunHeader.LastSpectrum;
                        spectraCount = rawFile.RunHeaderEx.SpectraCount;
                        var scan = rawFile.GetScanEventForScanNumber(lastSpectrum);

                        Console.WriteLine($"Seconds since file creation: {(DateTime.Now - created).TotalSeconds}");
                        Console.WriteLine($"Current retention time: {rawFile.RetentionTimeFromScanNumber(lastSpectrum)}");
                        Console.WriteLine($"Estimated time until end of run: {rawFile.RunHeader.ExpectedRuntime - rawFile.RetentionTimeFromScanNumber(lastSpectrum)}");
                        Console.WriteLine($"Latest spectrum: {lastSpectrum}\tTotal spectra: {spectraCount}");
                        Console.WriteLine($"Base intensity of latest scan: {rawFile.GetSegmentedScanFromScanNumber(lastSpectrum, null).Intensities.Max()}");
                        Console.WriteLine($"MS order of latest scan: {scan.MSOrder}");
                        Console.WriteLine();

                        f.WriteLine($"Seconds since file creation: {(DateTime.Now - created).TotalSeconds}");
                        f.WriteLine($"Current retention time: {rawFile.RetentionTimeFromScanNumber(lastSpectrum)}");
                        f.WriteLine($"Estimated time until end of run: {rawFile.RunHeader.ExpectedRuntime - rawFile.RetentionTimeFromScanNumber(lastSpectrum)}");
                        f.WriteLine($"Latest spectrum: {lastSpectrum}\tTotal spectra: {spectraCount}");
                        f.WriteLine($"Base intensity of latest scan: {rawFile.GetSegmentedScanFromScanNumber(lastSpectrum, null).Intensities.Max()}");
                        f.WriteLine($"MS order of latest scan: {scan.MSOrder}");
                        f.WriteLine();

                        Task.Delay(30000).Wait();
                    }

                    f.WriteLine($"{rawFile.FileName} reports it is done being acquired!");
                    Console.WriteLine($"{rawFile.FileName} reports it is done being acquired! Press any key to exit.");
                    Console.ReadKey();
                    Environment.Exit(0);
                }
            }
        }
    }
}