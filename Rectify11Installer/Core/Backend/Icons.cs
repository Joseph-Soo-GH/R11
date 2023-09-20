﻿using Microsoft.VisualBasic;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using static System.Environment;

namespace Rectify11Installer.Core
{
    internal class Icons
    {
        #region Variables
        private enum PatchType
        {
            General = 0,
            Mui,
            Troubleshooter,
            x86
        }
        #endregion
        public static bool Install(FrmWizard frm)
        {
            Logger.WriteLine("Installing icons");
            Logger.WriteLine("────────────────");
            // extract files, delete if folder exists
            frm.InstallerProgress = "Extracting files...";
            if (Directory.Exists(Path.Combine(Variables.r11Folder, "files")))
            {
                try
                {
                    Directory.Delete(Path.Combine(Variables.r11Folder, "files"), true);
                    Logger.WriteLine(Path.Combine(Variables.r11Folder, "files") + " exists. Deleting it.");
                }
                catch (Exception ex)
                {
                    Logger.WriteLine("Error deleting " + Path.Combine(Variables.r11Folder, "files"), ex);
                }
            }
            try
            {
                File.WriteAllBytes(Path.Combine(Variables.r11Folder, "files.7z"), Properties.Resources.files7z);
                Logger.LogFile("files.7z");
            }
            catch (Exception ex)
            {
                Logger.LogFile("files.7z", ex);
                return false;
            }

            // extract the 7z
            Helper.SvExtract("files.7z", "files");
            Logger.WriteLine("Extracted files.7z");

            // Get all patches
            var patches = PatchesParser.GetAll();
            var patch = patches.Items;
            decimal progress = 0;
            List<string> fileList = new();
            List<string> x86List = new();
            for (var i = 0; i < patch.Length; i++)
            {
                for (var j = 0; j < InstallOptions.iconsList.Count; j++)
                {
                    if (patch[i].Mui.Contains(InstallOptions.iconsList[j]))
                    {
                        var number = Math.Round((progress / InstallOptions.iconsList.Count) * 100m);
                        frm.InstallerProgress = "Patching " + patch[i].Mui + " (" + number + "%)";
                        if (!MatchAndApplyRule(patch[i]))
                        {
                            Logger.Warn("MatchAndApplyRule() on " + patch[i].Mui + " failed");
                        }
                        else
                        {
                            fileList.Add(patch[i].HardlinkTarget);
                            if (!string.IsNullOrWhiteSpace(patch[i].x86))
                            {
                                x86List.Add(patch[i].HardlinkTarget);
                            }
                        }
                        progress++;
                    }
                }
            }
            Logger.WriteLine("MatchAndApplyRule() succeeded");

            if (!WritePendingFiles(fileList, x86List))
            {
                Logger.WriteLine("WritePendingFiles() failed");
                return false;
            }
            Logger.WriteLine("WritePendingFiles() succeeded");

            if (!Common.WriteFiles(true, false))
            {
                Logger.WriteLine("WriteFiles() failed");
                return false;
            }
            Logger.WriteLine("WriteFiles() succeeded");

            frm.InstallerProgress = "Replacing files";

            // runs only if SSText3D.scr is selected
            if (InstallOptions.iconsList.Contains("SSText3D.scr"))
            {
                Interaction.Shell(Path.Combine(Variables.sys32Folder, "reg.exe") + " import " + Path.Combine(Variables.r11Files, "screensaver.reg"), AppWinStyle.Hide);
                Logger.WriteLine("screensaver.reg succeeded");
            }

            // runs only if any one of mmcbase.dll.mun, mmc.exe.mui or mmcndmgr.dll.mun is selected
            if (InstallOptions.iconsList.Contains("mmcbase.dll.mun")
                || InstallOptions.iconsList.Contains("mmc.exe.mui")
                || InstallOptions.iconsList.Contains("mmcndmgr.dll.mun"))
            {
                if (!MMCHelper.PatchAll())
                {
                    Logger.WriteLine("MmcHelper.PatchAll() failed");
                    return false;
                }
                Logger.WriteLine("MmcHelper.PatchAll() succeeded");
            }

            if (InstallOptions.iconsList.Contains("odbcad32.exe"))
            {
                if (!FixOdbc())
                {
                    Logger.Warn("FixOdbc() failed");
                }
                else
                {
                    Logger.WriteLine("FixOdbc() succeeded");
                }
            }
            // phase 2
            Interaction.Shell(Path.Combine(Variables.r11Folder, "aRun.exe")
                + " /EXEFilename " + '"' + Path.Combine(Variables.r11Folder, "Rectify11.Phase2.exe") + '"'
                + " /CommandLine " + "\'" + "/install" + "\'"
                + " /WaitProcess 1 /RunAs 8 /Run", AppWinStyle.NormalFocus, true);

            // reg files for various file extensions
            Interaction.Shell(Path.Combine(Variables.sys32Folder, "reg.exe") + " import " + Path.Combine(Variables.r11Files, "icons.reg"), AppWinStyle.Hide);
            Logger.WriteLine("icons.reg succeeded");

            Variables.RestartRequired = true;
            return true;
        }
        /// <summary>
        /// fixes 32-bit odbc shortcut icon
        /// </summary>
        private static bool FixOdbc()
        {
            var filename = string.Empty;
            var admintools = Path.Combine(Environment.GetFolderPath(SpecialFolder.CommonApplicationData), "Microsoft", "Windows", "Start Menu", "Programs", "Administrative Tools");
            var files = Directory.GetFiles(admintools);
            for (var i = 0; i < files.Length; i++)
            {
                if (!Path.GetFileName(files[i]).Contains("ODBC") ||
                    !Path.GetFileName(files[i])!.Contains("32")) continue;
                filename = Path.GetFileName(files[i]);
                File.Delete(files[i]);
            }
            try
            {
                using ShellLink shortcut = new();
                shortcut.Target = Path.Combine(Variables.sysWOWFolder, "odbcad32.exe");
                shortcut.WorkingDirectory = @"%windir%\system32";
                shortcut.IconPath = Path.Combine(Variables.sys32Folder, "odbcint.dll");
                shortcut.IconIndex = 0;
                shortcut.DisplayMode = ShellLink.LinkDisplayMode.edmNormal;
                if (filename != null) shortcut.Save(Path.Combine(admintools, filename));
            }
            catch
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// sets required registry values for phase 2
        /// </summary>
        /// <param name="fileList">normal files list</param>
        /// <param name="x86List">32-bit files list</param>
        private static bool WritePendingFiles(List<string> fileList, List<string> x86List)
        {
            using var reg = Registry.LocalMachine.OpenSubKey(@"SOFTWARE", true)?.CreateSubKey("Rectify11", true);
            if (reg == null) return false;
            try
            {
                reg.SetValue("PendingFiles", fileList.ToArray());
                Logger.WriteLine("Wrote filelist to PendingFiles");
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Error writing filelist to PendingFiles", ex);
                return false;
            }

            if (x86List.Count != 0)
            {
                try
                {
                    reg.SetValue("x86PendingFiles", x86List.ToArray());
                    Logger.WriteLine("Wrote x86list to x86PendingFiles");
                }
                catch (Exception ex)
                {
                    Logger.WriteLine("Error writing x86list to x86PendingFiles", ex);
                    return false;
                }
            }
            try
            {
                reg.SetValue("Language", CultureInfo.CurrentUICulture.Name);
                Logger.WriteLine("Wrote CurrentUICulture.Name to Language");
            }
            catch (Exception ex)
            {
                Logger.Warn("Error writing CurrentUICulture.Name to Language", ex);
            }
            try
            {
                reg.SetValue("Version", Assembly.GetEntryAssembly()?.GetName().Version);
                Logger.WriteLine("Wrote ProductVersion to Version");
            }
            catch (Exception ex)
            {
                Logger.Warn("Error writing ProductVersion to Version", ex);
            }


            try
            {
                reg?.SetValue("WindowsUpdate", Variables.WindowsUpdate ? 1 : 0);
                string sr = Variables.WindowsUpdate ? "1" : "0";
                Logger.WriteLine("Wrote " + sr + "to WindowsUpdate");
            }
            catch (Exception ex)
            {
                Logger.Warn("Error writing to WindowsUpdate", ex);
            }

            try
            {
                // mane fuck this shit
                using var ubrReg = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", false);
                reg.SetValue("OSVersion", OSVersion.Version.Major + "." + OSVersion.Version.Minor + "." + OSVersion.Version.Build + "." + ubrReg.GetValue("UBR").ToString());
                Logger.WriteLine("Wrote OSVersion");
            }
            catch (Exception ex)
            {
                Logger.Warn("Error writing OSVersion", ex);
            }
            return true;
        }

        /// <summary>
        /// Patches a specific file
        /// </summary>
        /// <param name="file">The file to be patched</param>
        /// <param name="patch">Xml element containing all the info</param>
        /// <param name="type">The type of the file to be patched.</param>
        private static bool Patch(string file, PatchesPatch patch, PatchType type)
        {
            if (File.Exists(file))
            {
                string name;
                string backupfolder;
                string tempfolder;
                if (type == PatchType.Troubleshooter)
                {
                    name = patch.Mui.Replace("Troubleshooter: ", "DiagPackage") + ".dll";
                    backupfolder = Path.Combine(Variables.r11Folder, "backup", "Diag");
                    tempfolder = Path.Combine(Variables.r11Folder, "Tmp", "Diag");
                }
                else if (type == PatchType.x86)
                {
                    var ext = Path.GetExtension(patch.Mui);
                    name = Path.GetFileNameWithoutExtension(patch.Mui) + "86" + ext;
                    backupfolder = Path.Combine(Variables.r11Folder, "backup");
                    tempfolder = Path.Combine(Variables.r11Folder, "Tmp");
                }
                else
                {
                    name = patch.Mui;
                    backupfolder = Path.Combine(Variables.r11Folder, "backup");
                    tempfolder = Path.Combine(Variables.r11Folder, "Tmp");
                }

                if (string.IsNullOrWhiteSpace(name))
                {
                    return false;
                }

                if (type == PatchType.Troubleshooter)
                {
                    if (!Directory.Exists(backupfolder))
                    {
                        Directory.CreateDirectory(backupfolder);
                    }
                    if (!Directory.Exists(tempfolder))
                    {
                        Directory.CreateDirectory(tempfolder);
                    }
                }

                //File.Copy(file, Path.Combine(backupfolder, name));
                File.Copy(file, Path.Combine(tempfolder, name), true);

                var filename = name + ".res";
                var masks = patch.mask;
                string filepath;
                if (type == PatchType.Troubleshooter)
                {
                    filepath = Path.Combine(Variables.r11Files, "Diag");
                }
                else
                {
                    filepath = Variables.r11Files;
                }

                if (patch.mask.Contains("|"))
                {
                    if (!string.IsNullOrWhiteSpace(patch.Ignore) && ((!string.IsNullOrWhiteSpace(patch.MinVersion) && OSVersion.Version.Build <= Int32.Parse(patch.MinVersion)) || (!string.IsNullOrWhiteSpace(patch.MaxVersion) && OSVersion.Version.Build >= Int32.Parse(patch.MaxVersion))))
                    {
                        masks = masks.Replace(patch.Ignore, "");
                    }
                    var str = masks.Split('|');
                    for (var i = 0; i < str.Length; i++)
                    {
                        if (type == PatchType.x86)
                        {
                            filename = Path.GetFileNameWithoutExtension(name).Remove(Path.GetFileNameWithoutExtension(name).Length - 2, 2) + Path.GetExtension(name) + ".res";
                        }
                        if (type != PatchType.Mui)
                        {
                            Interaction.Shell(Path.Combine(Variables.r11Folder, "ResourceHacker.exe") +
                            " -open " + Path.Combine(tempfolder, name) +
                            " -save " + Path.Combine(tempfolder, name) +
                            " -action " + "delete" +
                            " -mask " + str[i], AppWinStyle.Hide, true);
                        }
                        Interaction.Shell(Path.Combine(Variables.r11Folder, "ResourceHacker.exe") +
                        " -open " + Path.Combine(tempfolder, name) +
                        " -save " + Path.Combine(tempfolder, name) +
                        " -action " + "addskip" +
                        " -resource " + Path.Combine(filepath, filename) +
                        " -mask " + str[i], AppWinStyle.Hide, true);
                    }
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(patch.Ignore) && ((!string.IsNullOrWhiteSpace(patch.MinVersion) && OSVersion.Version.Build <= Int32.Parse(patch.MinVersion)) || (!string.IsNullOrWhiteSpace(patch.MaxVersion) && OSVersion.Version.Build >= Int32.Parse(patch.MaxVersion))))
                    {
                        masks = masks.Replace(patch.Ignore, "");
                    }
                    if (type == PatchType.x86)
                    {
                        filename = Path.GetFileNameWithoutExtension(name).Remove(Path.GetFileNameWithoutExtension(name).Length - 2, 2) + Path.GetExtension(name) + ".res";
                    }
                    if (type != PatchType.Mui)
                    {
                        Interaction.Shell(Path.Combine(Variables.r11Folder, "ResourceHacker.exe") +
                             " -open " + Path.Combine(tempfolder, name) +
                             " -save " + Path.Combine(tempfolder, name) +
                             " -action " + "delete" +
                             " -mask " + masks, AppWinStyle.Hide, true);
                    }
                    Interaction.Shell(Path.Combine(Variables.r11Folder, "ResourceHacker.exe") +
                            " -open " + Path.Combine(tempfolder, name) +
                            " -save " + Path.Combine(tempfolder, name) +
                            " -action " + "addskip" +
                            " -resource " + Path.Combine(filepath, filename) +
                            " -mask " + masks, AppWinStyle.Hide, true);
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Replaces the path and patches the file accordingly.
        /// </summary>
        /// <param name="patch">Xml element containing all the info</param>
        private static bool MatchAndApplyRule(PatchesPatch patch)
        {
            string newhardlink;
            if (patch.HardlinkTarget.Contains("%sys32%"))
            {
                newhardlink = patch.HardlinkTarget.Replace(@"%sys32%", Variables.sys32Folder);
                if (!Patch(newhardlink, patch, PatchType.General))
                {
                    return false;
                }
            }
            else if (patch.HardlinkTarget.Contains("%lang%"))
            {
                newhardlink = patch.HardlinkTarget.Replace(@"%lang%", Path.Combine(Variables.sys32Folder, CultureInfo.CurrentUICulture.Name));
                if (!Patch(newhardlink, patch, PatchType.Mui))
                {
                    return false;
                }
            }
            else if (patch.HardlinkTarget.Contains("%en-US%"))
            {
                newhardlink = patch.HardlinkTarget.Replace(@"%en-US%", Path.Combine(Variables.sys32Folder, "en-US"));
                if (!Patch(newhardlink, patch, PatchType.Mui))
                {
                    return false;
                }
            }
            else if (patch.HardlinkTarget.Contains("%windirLang%"))
            {
                newhardlink = patch.HardlinkTarget.Replace(@"%windirLang%", Path.Combine(Variables.Windir, CultureInfo.CurrentUICulture.Name));
                if (!Patch(newhardlink, patch, PatchType.Mui))
                {
                    return false;
                }
            }
            else if (patch.HardlinkTarget.Contains("%windirEn-US%"))
            {
                newhardlink = patch.HardlinkTarget.Replace(@"%windirEn-US%", Path.Combine(Variables.Windir, "en-US"));
                if (!Patch(newhardlink, patch, PatchType.Mui))
                {
                    return false;
                }
            }
            else if (patch.HardlinkTarget.Contains("mun"))
            {
                newhardlink = patch.HardlinkTarget.Replace(@"%sysresdir%", Variables.sysresdir);
                if (!Patch(newhardlink, patch, PatchType.General))
                {
                    return false;
                }
            }
            else if (patch.HardlinkTarget.Contains("%branding%"))
            {
                newhardlink = patch.HardlinkTarget.Replace(@"%branding%", Variables.BrandingFolder);
                if (!Patch(newhardlink, patch, PatchType.General))
                {
                    return false;
                }
            }
            else if (patch.HardlinkTarget.Contains("%prog%"))
            {
                newhardlink = patch.HardlinkTarget.Replace(@"%prog%", Variables.progfiles);
                if (!Patch(newhardlink, patch, PatchType.General))
                {
                    return false;
                }
            }
            else if (patch.HardlinkTarget.Contains("%diag%"))
            {
                newhardlink = patch.HardlinkTarget.Replace(@"%diag%", Variables.diag);
                if (!Patch(newhardlink, patch, PatchType.Troubleshooter))
                {
                    return false;
                }
            }
            else if (patch.HardlinkTarget.Contains("%windir%"))
            {
                newhardlink = patch.HardlinkTarget.Replace(@"%windir%", Variables.Windir);
                if (!Patch(newhardlink, patch, PatchType.General))
                {
                    return false;
                }
            }
            if (!string.IsNullOrWhiteSpace(patch.x86))
            {
                if (patch.HardlinkTarget.Contains("%sys32%"))
                {
                    newhardlink = patch.HardlinkTarget.Replace(@"%sys32%", Variables.sysWOWFolder);
                    if (!Patch(newhardlink, patch, PatchType.x86))
                    {
                        return false;
                    }
                }
                else if (patch.HardlinkTarget.Contains("%prog%"))
                {
                    newhardlink = patch.HardlinkTarget.Replace(@"%prog%", Variables.progfiles86);
                    if (!Patch(newhardlink, patch, PatchType.x86))
                    {
                        return false;
                    }
                }
            }
            return true;
        }
    }
}