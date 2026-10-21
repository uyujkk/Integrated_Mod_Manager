// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 EndfieldBridge contributors and Integrated Mod Manager contributors.
// Modified 2026-10-21: integrated into Integrated Mod Manager; see NOTICE.md.
namespace IntegratedModManager.EndfieldLaunch;

/// <summary>A user-facing message in both supported UI languages.</summary>
public readonly record struct LaunchText(string Zh, string En)
{
    public string For(bool english) => english ? En : Zh;

    public static LaunchText operator +(LaunchText left, LaunchText right) =>
        new(left.Zh + right.Zh, left.En + right.En);

    public static LaunchText Both(string value) => new(value, value);
}

/// <summary>An expected launch or setup failure with a bilingual explanation.</summary>
public class EndfieldLaunchException : Exception
{
    public EndfieldLaunchException(LaunchText text, Exception? innerException = null)
        : base(text.En, innerException) => Text = text;

    public EndfieldLaunchException(string zh, string en, Exception? innerException = null)
        : this(new LaunchText(zh, en), innerException)
    {
    }

    public LaunchText Text { get; }
}

/// <summary>The candidate is not an official, validly signed launcher; another candidate may be tried.</summary>
public sealed class OfficialSignatureException : EndfieldLaunchException
{
    public OfficialSignatureException(string zh, string en) : base(zh, en)
    {
    }
}
