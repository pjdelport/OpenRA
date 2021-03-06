#region Copyright & License Information
/*
 * Copyright 2007-2015 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation. For more information,
 * see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using OpenRA.FileFormats;

namespace OpenRA.FileSystem
{
	public sealed class InstallShieldPackage : IFolder
	{
		readonly Dictionary<uint, PackageEntry> index = new Dictionary<uint, PackageEntry>();
		readonly List<string> filenames;
		readonly Stream s;
		readonly long dataStart = 255;
		readonly int priority;
		readonly string filename;

		public InstallShieldPackage(FileSystem context, string filename, int priority)
		{
			this.filename = filename;
			this.priority = priority;

			filenames = new List<string>();

			s = context.Open(filename);
			try
			{
				// Parse package header
				var reader = new BinaryReader(s);
				var signature = reader.ReadUInt32();
				if (signature != 0x8C655D13)
					throw new InvalidDataException("Not an Installshield package");

				reader.ReadBytes(8);
				/*var FileCount = */reader.ReadUInt16();
				reader.ReadBytes(4);
				/*var ArchiveSize = */reader.ReadUInt32();
				reader.ReadBytes(19);
				var tocAddress = reader.ReadInt32();
				reader.ReadBytes(4);
				var dirCount = reader.ReadUInt16();

				// Parse the directory list
				s.Seek(tocAddress, SeekOrigin.Begin);
				var tocReader = new BinaryReader(s);

				var fileCountInDirs = new List<uint>();

				// Parse directories
				for (var i = 0; i < dirCount; i++)
					fileCountInDirs.Add(ParseDirectory(tocReader));

				// Parse files
				foreach (var fileCount in fileCountInDirs)
					for (var i = 0; i < fileCount; i++)
						ParseFile(reader);
			}
			catch
			{
				Dispose();
				throw;
			}
		}

		static uint ParseDirectory(BinaryReader reader)
		{
			// Parse directory header
			var fileCount = reader.ReadUInt16();
			var chunkSize = reader.ReadUInt16();
			var nameLength = reader.ReadUInt16();
			reader.ReadChars(nameLength); // var DirName = new String(reader.ReadChars(NameLength));

			// Skip to the end of the chunk
			reader.ReadBytes(chunkSize - nameLength - 6);
			return fileCount;
		}

		uint accumulatedData = 0;
		void ParseFile(BinaryReader reader)
		{
			reader.ReadBytes(7);
			var compressedSize = reader.ReadUInt32();
			reader.ReadBytes(12);
			var chunkSize = reader.ReadUInt16();
			reader.ReadBytes(4);
			var nameLength = reader.ReadByte();
			var fileName = new string(reader.ReadChars(nameLength));

			var hash = PackageEntry.HashFilename(fileName, PackageHashType.Classic);
			if (!index.ContainsKey(hash))
				index.Add(hash, new PackageEntry(hash, accumulatedData, compressedSize));
			filenames.Add(fileName);
			accumulatedData += compressedSize;

			// Skip to the end of the chunk
			reader.ReadBytes(chunkSize - nameLength - 30);
		}

		public Stream GetContent(uint hash)
		{
			PackageEntry e;
			if (!index.TryGetValue(hash, out e))
				return null;

			s.Seek(dataStart + e.Offset, SeekOrigin.Begin);
			var data = s.ReadBytes((int)e.Length);

			return new MemoryStream(Blast.Decompress(data));
		}

		public Stream GetContent(string filename)
		{
			return GetContent(PackageEntry.HashFilename(filename, PackageHashType.Classic));
		}

		public IEnumerable<uint> ClassicHashes()
		{
			return index.Keys;
		}

		public IEnumerable<uint> CrcHashes()
		{
			yield break;
		}

		public IEnumerable<string> AllFileNames()
		{
			return filenames;
		}

		public bool Exists(string filename)
		{
			return index.ContainsKey(PackageEntry.HashFilename(filename, PackageHashType.Classic));
		}

		public int Priority { get { return 2000 + priority; } }
		public string Name { get { return filename; } }

		public void Write(Dictionary<string, byte[]> contents)
		{
			throw new NotImplementedException("Cannot save InstallShieldPackages.");
		}

		public void Dispose()
		{
			s.Dispose();
		}
	}
}
