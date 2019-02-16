﻿using BleemSync.Data;
using BleemSync.Data.Entities;
using BleemSync.Data.Models;
using BleemSync.Services.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace BleemSync.Extensions.PlayStationClassic.Core.Services
{
    public class GameManagerService : IGameManagerService
    {
        private MenuDatabaseContext _context { get; set; }
        private IConfiguration _configuration { get; set; }
        private string _baseGamesDirectory { get; set; }

        public GameManagerService(MenuDatabaseContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;

            _baseGamesDirectory = Path.Combine(
                configuration["BleemSync:Destination"],
                configuration["BleemSync:PlayStationClassic:GamesDirectory"]
            );
        }

        public void AddGame(GameManagerNode node)
        {
            var game = new Game()
            {
                Id = node.Id,
                Title = node.Name,
                Publisher = node.Publisher,
                Year = node.ReleaseDate.HasValue ? node.ReleaseDate.Value.Year : 0,
                Players = node.Players.HasValue ? node.Players.Value : 0,
                Position = node.Position
            };

            _context.Games.Add(game);
            _context.SaveChanges();

            if (node.Files.Count > 0)
            {
                // Move the files to the correct location and update the BleemSync database to reflect where the files are moved to
                var outputDirectory = Path.Combine(_baseGamesDirectory, game.Id.ToString());

                Directory.CreateDirectory(outputDirectory);

                PostProcessGameFiles(node.Files, outputDirectory);
            }
        }

        private void PostProcessGameFiles(List<GameManagerFile> files, string outputDirectory)
        {
            var additionalFiles = new List<GameManagerFile>();

            foreach (var file in files)
            {
                var source = file.Path;
                var destination = Path.Combine(outputDirectory, file.Name);
                var extension = Path.GetExtension(file.Name);
                // Lowercase extension for consistency, except for .bin, which may be
                // case-sensitively referenced by .cue files
                if (extension.ToLower() != ".bin") Path.ChangeExtension(destination, extension.ToLower());

                SystemMove(source, destination);
                file.Path = Path.GetFullPath(destination);

                var fileInfo = new FileInfo(destination);

                switch (fileInfo.Extension.ToLower())
                {
                    case ".pbp":
                        break;
                }
            }

            var cueFiles = files.Where(f => Path.GetExtension(f.Name).ToLower() == ".cue");
            var firstDiscFile = cueFiles.Select(f => f.Name).FirstOrDefault();
            if (firstDiscFile == null) firstDiscFile = files.First().Name;
            var baseName = Path.GetFileNameWithoutExtension(firstDiscFile);
            var coverFile = files.Where(f => f.Name == "cover.png").FirstOrDefault();
            if (coverFile != null)
            {
                var newCoverFileName = baseName + ".png";
                var newCoverFilePath = Path.Combine(outputDirectory, newCoverFileName);

                SystemMove(coverFile.Path, newCoverFilePath);
                coverFile.Path = Path.GetFullPath(newCoverFilePath);
                coverFile.Name = newCoverFileName;
            }

            files.AddRange(additionalFiles);

            var discNum = 1;
            foreach (var cueFile in cueFiles)
            {
                var disc = new Disc()
                {
                    DiscBasename = Path.ChangeExtension(cueFile.Name, null),
                    DiscNumber = discNum,
                    GameId = cueFile.NodeId,
                };

                _context.Discs.Add(disc);

                discNum++;
            }

            _context.SaveChanges();
        }

        static void SystemMove(string from, string to)
        {
            SystemMove(from, to, false);
        }

        // HACK: File.Move() seems to cause a copy. Try to mv with shell and see if it's faster.
        static void SystemMove(string from, string to, bool overwrite)
        {
            if (overwrite && File.Exists(to)) throw new IOException("Destination file exists.");
            from = from.Replace("\"", "\\\"");
            to = to.Replace("\"", "\\\"");
            var proc = System.Diagnostics.Process.Start("mv", $"{(overwrite ? "-f " : string.Empty)}\"{from}\" \"{to}\"");
            proc.WaitForExit();
            if (proc.ExitCode != 0) throw new IOException($"mv returned {proc.ExitCode}");
        }

        private GameManagerFile CreateCueSheet(FileInfo sourceFileInfo, GameManagerFile sourceFile)
        {
            var managerFile = new GameManagerFile();
            var sb = new StringBuilder();

            sb.AppendLine($"FILE \"{sourceFileInfo.Name}\" BINARY");
            sb.AppendLine("  TRACK 01 MODE2/2352");
            sb.AppendLine("    INDEX 01 00:00:00");

            var cueSheetFileName = Path.ChangeExtension(sourceFile.Name, ".cue");

            File.WriteAllText(
                Path.Combine(
                    sourceFileInfo.Directory.FullName,
                    cueSheetFileName),
                sb.ToString());

            managerFile.Name = cueSheetFileName;
            managerFile.Path = sourceFile.Path;
            managerFile.NodeId = sourceFile.NodeId;

            return managerFile;
        }

        public void UpdateGame(GameManagerNode node)
        {
            UpdateGames(new GameManagerNode[] { node });
        }

        public void UpdateGames(IEnumerable<GameManagerNode> nodes)
        {
            foreach (var node in nodes)
            {
                var isNewEntry = false;
                var game = _context.Games.Find(node.Id);

                if (game == null)
                {
                    game = new Game();
                    game.Id = node.Id;
                    isNewEntry = true;
                }

                game.Title = node.Name;
                game.Publisher = node.Publisher;
                game.Year = node.ReleaseDate.HasValue ? node.ReleaseDate.Value.Year : 0;
                game.Players = node.Players.HasValue ? node.Players.Value : 0;
                game.Position = node.Position;

                if (isNewEntry)
                {
                    _context.Games.Add(game);
                }
                else
                {
                    _context.Games.Update(game);
                }
            }

            _context.SaveChanges();
        }

        public void DeleteGame(GameManagerNode node)
        {
            // Delete files first
            var gameDirectory = Path.Combine(_baseGamesDirectory, node.Id.ToString());
            if (Directory.Exists(gameDirectory)) Directory.Delete(gameDirectory, true);

            var game = _context.Games.Find(node.Id);

            _context.Games.Remove(game);
            _context.SaveChanges();
        }

        public void RebuildDatabase(IEnumerable<GameManagerNode> nodes)
        {
            _context.Database.EnsureDeleted();
            _context.Database.Migrate();

            foreach (var node in nodes)
            {
                var game = new Game()
                {
                    Id = node.Id,
                    Title = node.Name,
                    Publisher = node.Publisher,
                    Year = node.ReleaseDate.HasValue ? node.ReleaseDate.Value.Year : 0,
                    Players = node.Players.HasValue ? node.Players.Value : 0,
                    Position = node.Position
                };

                _context.Games.Add(game);

                if (node.Files.Count > 0)
                {
                    var cueFiles = node.Files.Where(f => Path.GetExtension(f.Name).ToLower() == ".cue");

                    var discNum = 1;

                    foreach (var cueFile in cueFiles)
                    {
                        var disc = new Disc()
                        {
                            DiscBasename = Path.ChangeExtension(cueFile.Name, null),
                            DiscNumber = discNum,
                            GameId = cueFile.NodeId,
                        };

                        _context.Discs.Add(disc);

                        discNum++;
                    }
                }

                _context.SaveChanges();
            }
        }

        public IEnumerable<GameManagerNode> GetGames()
        {
            List<GameManagerNode> nodes = new List<GameManagerNode>();
            foreach (var game in _context.Games)
            {
                var node = new GameManagerNode
                {
                    Id = game.Id,
                    Name = game.Title,
                    SortName = game.Title,
                    ReleaseDate = new DateTime(game.Year, 1, 1),
                    Players = game.Players,
                    Publisher = game.Publisher,
                    Type = GameManagerNodeType.Game
                };

                string gameDir = Path.Combine(_baseGamesDirectory, game.Id.ToString());
                // If user for some reason doesn't have the game files, don't return game
                if (Directory.Exists(gameDir))
                {
                    // For files, iterate what we have on disk instead of looking in the database
                    foreach (var path in Directory.GetFiles(gameDir))
                    {
                        node.Files.Add(new GameManagerFile
                        {
                            Name = Path.GetFileName(path),
                            Path = Path.GetFullPath(path),
                            Node = node
                        });
                    }

                    nodes.Add(node);
                }
            }

            return nodes;
        }

        public void Sync()
        {
            // Assuming everything's going OK, we can tell power_manage to reboot,
            // and any additional setup needed will be done on next boot
            File.WriteAllText("/dev/shm/power/control", "reboot");
        }
    }
}
