﻿using System.IO;
using System.Threading.Tasks;

namespace Downloader
{
    public abstract class ChunkProvider
    {
        protected ChunkProvider(DownloadConfiguration config)
        {
            Configuration = config;
        }

        protected DownloadConfiguration Configuration { get; }

        public Chunk[] ChunkFile(long fileSize, int parts)
        {
            if (parts < 1)
            {
                parts = 1;
            }

            long chunkSize = fileSize / parts;

            if (chunkSize < 1)
            {
                chunkSize = 1;
                parts = (int)fileSize;
            }

            Chunk[] chunks = new Chunk[parts];
            for (int i = 0; i < parts; i++)
            {
                bool isLastChunk = i == parts - 1;
                long startPosition = i * chunkSize;
                long endPosition = isLastChunk ? fileSize - 1 : (startPosition + chunkSize) - 1;

                Chunk chunk = Factory(startPosition, endPosition);
                chunk.MaxTryAgainOnFailover = Configuration.MaxTryAgainOnFailover;
                chunk.Timeout = Configuration.Timeout;
                chunks[i] = chunk;
            }

            return chunks;
        }

        protected abstract Chunk Factory(long startPosition, long endPosition);
        public abstract Task MergeChunks(Chunk[] chunks, string targetFileName);

        protected Stream CreateFile(string filename)
        {
            string directory = Path.GetDirectoryName(filename);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return Stream.Null;
            }

            if (Directory.Exists(directory) == false)
            {
                Directory.CreateDirectory(directory);
            }

            return new FileStream(filename, FileMode.Append, FileAccess.Write);
        }

        public abstract ChunkDownloader GetChunkDownloader(Chunk chunk);
    }
}