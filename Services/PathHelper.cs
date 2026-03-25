// Copyright (c) BootstrapBlazor & Argo Zhang (argo@live.ca). All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.
// Website: https://www.blazor.zone

namespace BootstrapBlazor.McpServer.Services;

/// <summary>
/// Helper class to locate the base application folder by walking up from the executable location
/// </summary>
public static class PathHelper
{
    private static string? _basePath;
    private static string? _dataPath;

    /// <summary>
    /// Gets the base application path by walking up from the executable location
    /// until the wwwroot folder is found
    /// </summary>
    public static string GetBasePath()
    {
        if (_basePath != null)
            return _basePath;

        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var dir = new DirectoryInfo(baseDir);

        // Walk up to find the actual application root (where wwwroot exists)
        while (dir != null)
        {
            if (dir.GetDirectories("wwwroot").Any())
            {
                _basePath = dir.FullName;
                return _basePath;
            }
            dir = dir.Parent;
        }

        // Fallback to base directory if wwwroot not found
        _basePath = baseDir;
        return _basePath;
    }

    /// <summary>
    /// Gets the data folder path (relative to base path)
    /// </summary>
    public static string GetDataPath()
    {
        if (_dataPath == null)
        {
            _dataPath = Path.Combine(GetBasePath(), "data");
            if (!Directory.Exists(_dataPath))
            {
                Directory.CreateDirectory(_dataPath);
            }
        }
        return _dataPath;
    }

    /// <summary>
    /// Gets the wwwroot folder path
    /// </summary>
    public static string GetWwwRootPath()
    {
        return Path.Combine(GetBasePath(), "wwwroot");
    }

    /// <summary>
    /// Resets the cached paths (useful for testing)
    /// </summary>
    public static void Reset()
    {
        _basePath = null;
        _dataPath = null;
    }
}