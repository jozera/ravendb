﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Voron.Impl;
using Voron.Util;

namespace Voron
{
    public abstract class StorageEnvironmentOptions : IDisposable
    {
        public long MaxLogFileSize
        {
            get { return _maxLogFileSize; }
            set
            {
                if (value < _initialLogFileSize)
                    InitialLogFileSize = value;
                _maxLogFileSize = value;
            }
        }

        public long InitialLogFileSize
        {
            get { return _initialLogFileSize; }
            set
            {
                if (value > MaxLogFileSize)
                    MaxLogFileSize = value;
                _initialLogFileSize = value;
            }
        }

        public bool OwnsPagers { get; set; }

		public bool ManualFlushing { get; set; }

        public bool IncrementalBackupEnabled { get; set; }

        public abstract IVirtualPager DataPager { get; }

	    public long MaxNumberOfPagesInJournalBeforeFlush { get; set; }

	    public int IdleFlushTimeout { get; set; }

	    public abstract IVirtualPager CreateJournalPager(long number);

        protected bool Disposed;
        private long _initialLogFileSize;
        private long _maxLogFileSize;

        protected StorageEnvironmentOptions()
        {
	        MaxNumberOfPagesInJournalBeforeFlush = 1024; // 4 MB

	        IdleFlushTimeout = 5000; // 5 seconds

            MaxLogFileSize = 64 * 1024 * 1024;

            InitialLogFileSize = 64 * 1024;

            OwnsPagers = true;
            IncrementalBackupEnabled = false;
        }

        public static StorageEnvironmentOptions GetInMemory()
        {
            return new PureMemoryStorageEnvironmentOptions();
        }

        public static StorageEnvironmentOptions ForPath(string path, FlushMode flushMode = FlushMode.Full)
        {
            return new DirectoryStorageEnvironmentOptions(path, flushMode);
        }

		public IDisposable AllowManualFlushing()
		{
			var old = ManualFlushing;
			ManualFlushing = true;

			return new DisposableAction(() => ManualFlushing = old);
		}


        public class DirectoryStorageEnvironmentOptions : StorageEnvironmentOptions
        {
            private readonly FlushMode _flushMode;
            private readonly string _basePath;
            private readonly Lazy<IVirtualPager> _dataPager;

            private readonly ConcurrentDictionary<string, Lazy<IVirtualPager>> _journals =
                new ConcurrentDictionary<string, Lazy<IVirtualPager>>(StringComparer.OrdinalIgnoreCase);

            public DirectoryStorageEnvironmentOptions(string basePath, FlushMode flushMode)
            {
                _flushMode = flushMode;
                _basePath = Path.GetFullPath(basePath);
                _dataPager = new Lazy<IVirtualPager>(CreateDataPager);
            }

            private IVirtualPager CreateDataPager()
            {
                if (Directory.Exists(_basePath) == false)
                {
                    Directory.CreateDirectory(_basePath);
                }
                return new FilePager(Path.Combine(_basePath, "db.voron"), _flushMode);
            }

            public override IVirtualPager DataPager
            {
                get
                {
                    return _dataPager.Value;
                }
            }

            public override IVirtualPager CreateJournalPager(long number)
            {
	            var name = LogName(number);
				var path = Path.Combine(_basePath, name);
                var orAdd = _journals.GetOrAdd(name, _ => new Lazy<IVirtualPager>(() => new MemoryMapPager(path, _flushMode)));

				if (orAdd.Value.Disposed)
				{
					var newPager = new Lazy<IVirtualPager>(() => new MemoryMapPager(path, _flushMode));
					if (_journals.TryUpdate(name, newPager, orAdd) == false)
						throw new InvalidOperationException("Could not update journal pager");
					orAdd = newPager;
				}

                return orAdd.Value;
            }

            public override void Dispose()
            {
                if (Disposed)
                    return;
                Disposed = true;
                if (_dataPager.IsValueCreated)
                    _dataPager.Value.Dispose();
                foreach (var journal in _journals)
                {
                    if (journal.Value.IsValueCreated)
                        journal.Value.Value.Dispose();
                }
            }

	        public override bool TryDeletePager(long number)
	        {
		        var name = LogName(number);
		        var file = Path.Combine(_basePath, name);
		        if (File.Exists(file) == false)
			        return false;
		        File.Delete(file);
		        return true;
	        }
        }

        public class PureMemoryStorageEnvironmentOptions : StorageEnvironmentOptions
        {
            private readonly PureMemoryPager _dataPager;

            private Dictionary<string, IVirtualPager> _logs =
                new Dictionary<string, IVirtualPager>(StringComparer.OrdinalIgnoreCase);

            public PureMemoryStorageEnvironmentOptions()
            {
                _dataPager = new PureMemoryPager();
            }

            public override IVirtualPager DataPager
            {
                get { return _dataPager; }
            }

            public override IVirtualPager CreateJournalPager(long number)
            {
	            var name = LogName(number);
                IVirtualPager value;
                if (_logs.TryGetValue(name, out value))
                    return value;
                value = new PureMemoryPager();
                _logs[name] = value;
                return value;
            }

            public override void Dispose()
            {
                if (Disposed)
                    return;
                Disposed = true;

                _dataPager.Dispose();
                foreach (var virtualPager in _logs)
                {
                    virtualPager.Value.Dispose();
                }
            }

	        public override bool TryDeletePager(long number)
	        {
		        var name = LogName(number);
		        IVirtualPager value;
		        if (_logs.TryGetValue(name, out value) == false)
			        return false;
		        _logs.Remove(name);
				value.Dispose();
		        return true;
	        }
        }

	    public static string LogName(long number)
		{
			return string.Format("{0:D19}.journal", number);
		}

        public abstract void Dispose();

	    public abstract bool TryDeletePager(long number);
    }

}