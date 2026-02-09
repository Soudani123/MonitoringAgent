using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;

namespace MonitoringAgent
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== Agent de Monitoring Windows (MVP) ===");

            // Audit système
            string machine = Environment.MachineName;
            string user = Environment.UserName;
            string os = Environment.OSVersion.ToString();
            string domain = Environment.UserDomainName;

            // Inventaire des applications
            List<object> apps = new List<object>();
            string uninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
            using (var baseKey = Registry.LocalMachine.OpenSubKey(uninstallKey))
            {
                if (baseKey != null)
                {
                    foreach (var subKeyName in baseKey.GetSubKeyNames())
                    {
                        using (var subKey = baseKey.OpenSubKey(subKeyName))
                        {
                            var name = subKey?.GetValue("DisplayName");
                            var version = subKey?.GetValue("DisplayVersion");
                            var publisher = subKey?.GetValue("Publisher");
                            if (name != null)
                            {
                                apps.Add(new { name, version, publisher });
                            }
                        }
                    }
                }
            }

            // Performance CPU/RAM
            var cpuUsage = GetCpuUsage();
            long ramProcessMB = Environment.WorkingSet / (1024 * 1024);

            List<object> disks = new List<object>();
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                if (drive.IsReady)
                {
                    disks.Add(new
                    {
                        name = drive.Name,
                        freeGB = drive.AvailableFreeSpace / (1024 * 1024 * 1024),
                        totalGB = drive.TotalSize / (1024 * 1024 * 1024)
                    });
                }
            }

            // Logs Windows (Application + System) depuis le 01/01/2026
            DateTime startDate = new DateTime(2026, 1, 1);
            List<object> logs = new List<object>();

            try
            {
                EventLog appLog = new EventLog("Application");
                foreach (EventLogEntry entry in appLog.Entries)
                {
                    if ((entry.EntryType == EventLogEntryType.Error || entry.EntryType == EventLogEntryType.Warning)
                        && entry.TimeGenerated >= startDate
                        && logs.Count < 50) // limiter à 50 derniers
                    {
                        string niveau = entry.EntryType == EventLogEntryType.Error ? "Critique" : "Moyen";

                        logs.Add(new
                        {
                            time = entry.TimeGenerated,
                            type = entry.EntryType.ToString(),
                            source = entry.Source,
                            message = entry.Message,
                            niveau
                        });
                    }
                }
            }
            catch { }

            try
            {
                EventLog sysLog = new EventLog("System");
                foreach (EventLogEntry entry in sysLog.Entries)
                {
                    if ((entry.EntryType == EventLogEntryType.Error || entry.EntryType == EventLogEntryType.Warning)
                        && entry.TimeGenerated >= startDate
                        && logs.Count < 100) // limiter à 50 + 50
                    {
                        string niveau = entry.EntryType == EventLogEntryType.Error ? "Critique" : "Moyen";

                        logs.Add(new
                        {
                            time = entry.TimeGenerated,
                            type = entry.EntryType.ToString(),
                            source = entry.Source,
                            message = entry.Message,
                            niveau
                        });
                    }
                }
            }
            catch { }

            // Préparation des données JSON enrichies
            var jsonData = new
            {
                machine,
                user,
                os,
                domain,
                cpu = cpuUsage,
                ramProcessMB,
                disks,
                applications = apps,
                logs
            };

            string jsonString = JsonSerializer.Serialize(jsonData);

            // Envoi vers Webhook.site
            var client = new HttpClient();
            var content = new StringContent(jsonString, Encoding.UTF8, "application/json");

            await client.PostAsync("https://webhook.site/3268c86a-3936-4bbc-a9a0-3b4fd9eb8525", content);

            Console.WriteLine("\n✅ Données enrichies envoyées vers Webhook.site !");
        }

        // Fonction pour CPU usage
        static string GetCpuUsage()
        {
            var cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            cpuCounter.NextValue();
            System.Threading.Thread.Sleep(1000); // attendre une seconde
            return cpuCounter.NextValue().ToString("F1") + " %";
        }
    }
}