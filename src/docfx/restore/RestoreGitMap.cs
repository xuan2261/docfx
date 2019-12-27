// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;

namespace Microsoft.Docs.Build
{
    internal class RestoreGitMap : IDisposable
    {
        private readonly string _docsetPath;
        private readonly ConcurrentDictionary<PathString, InterProcessReaderWriterLock> _locks = new ConcurrentDictionary<PathString, InterProcessReaderWriterLock>();
        private readonly DependencyLockProvider _dependencyLockProvider;

        private RestoreGitMap(DependencyLockProvider dependencyLockProvider, string docsetPath)
        {
            Debug.Assert(dependencyLockProvider != null);
            Debug.Assert(!string.IsNullOrEmpty(docsetPath));

            _docsetPath = docsetPath;
            _dependencyLockProvider = dependencyLockProvider;
        }

        public bool TryGetRestoreGitPath(PackagePath packagePath, RestoreGitFlags flags, out string path, out string commit)
        {
            try
            {
                (path, commit) = GetRestoreGitPath(packagePath, flags);
                return true;
            }
            catch (DocfxException)
            {
                path = commit = default;
                return false;
            }
        }

        public (string path, string commit) GetRestoreGitPath(PackagePath packagePath, RestoreGitFlags flags)
        {
            switch (packagePath.Type)
            {
                case PackageType.Folder:
                    var fullPath = Path.Combine(_docsetPath, packagePath.Path);
                    if (Directory.Exists(fullPath))
                    {
                        return (fullPath, default);
                    }

                    // TODO: populate source info
                    throw Errors.DirectoryNotFound(new SourceInfo<string>(packagePath.ToString())).ToException();

                case PackageType.Git:
                    var gitLock = _dependencyLockProvider.GetGitLock(packagePath.Url, packagePath.Branch);

                    if (gitLock is null || gitLock.Commit is null)
                    {
                        throw Errors.NeedRestore($"{packagePath}").ToException();
                    }

                    var path = AppData.GetGitDir(packagePath.Url);

                    if (!flags.HasFlag(RestoreGitFlags.Bare))
                    {
                        path = new PathString(Path.Combine(path, "1"));
                    }

                    if (!Directory.Exists(path))
                    {
                        throw Errors.NeedRestore($"{packagePath}").ToException();
                    }

                    _locks.TryAdd(path, InterProcessReaderWriterLock.CreateReaderLock(path));

                    return (path, gitLock.Commit);

                default:
                    throw new NotSupportedException($"Unknown package url: '{packagePath}'");
            }
        }

        public void Dispose()
        {
            foreach (var sharedLock in _locks.Values)
            {
                sharedLock.Dispose();
            }
        }

        /// <summary>
        /// Acquired all shared git based on dependency lock
        /// The dependency lock must be loaded before using this method
        /// </summary>
        public static RestoreGitMap Create(string docsetPath, string locale)
        {
            var dependencyLockProvider = DependencyLockProvider.CreateFromAppData(docsetPath, locale);

            return new RestoreGitMap(dependencyLockProvider, docsetPath);
        }
    }
}
