using System;
using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class ByteDiffTests
{
    [Fact]
    public void Compare_equal_frames_all_match_zero_diff()
    {
        var (states, diffCount) = ByteDiff.Compare(new byte[] { 0x01, 0x02, 0x03 }, new byte[] { 0x01, 0x02, 0x03 });

        Assert.Equal(3, states.Length);
        Assert.All(states, s => Assert.Equal(ByteDiff.ByteState.Match, s));
        Assert.Equal(0, diffCount);
    }

    [Fact]
    public void Compare_differing_byte_flagged()
    {
        var (states, diffCount) = ByteDiff.Compare(new byte[] { 0x01, 0xAA, 0x03 }, new byte[] { 0x01, 0xBB, 0x03 });

        Assert.Equal(ByteDiff.ByteState.Match, states[0]);
        Assert.Equal(ByteDiff.ByteState.Diff, states[1]);
        Assert.Equal(ByteDiff.ByteState.Match, states[2]);
        Assert.Equal(1, diffCount);
    }

    [Fact]
    public void Compare_unequal_lengths_extra_bytes_are_diff()
    {
        var (states, diffCount) = ByteDiff.Compare(new byte[] { 0x01, 0x02 }, new byte[] { 0x01, 0x02, 0x03, 0x04 });

        Assert.Equal(4, states.Length);
        Assert.Equal(ByteDiff.ByteState.Match, states[0]);
        Assert.Equal(ByteDiff.ByteState.Match, states[1]);
        Assert.Equal(ByteDiff.ByteState.Diff, states[2]);
        Assert.Equal(ByteDiff.ByteState.Diff, states[3]);
        Assert.Equal(2, diffCount);
    }

    [Fact]
    public void Compare_empty_side_counts_all_as_diff()
    {
        var (states, diffCount) = ByteDiff.Compare(Array.Empty<byte>(), new byte[] { 0x01, 0x02 });

        Assert.Equal(2, states.Length);
        Assert.All(states, s => Assert.Equal(ByteDiff.ByteState.Diff, s));
        Assert.Equal(2, diffCount);
    }

    [Fact]
    public void Compare_both_empty_zero_states()
    {
        var (states, diffCount) = ByteDiff.Compare(Array.Empty<byte>(), Array.Empty<byte>());

        Assert.Empty(states);
        Assert.Equal(0, diffCount);
    }

    [Fact]
    public void Compare_null_arguments_throw()
    {
        Assert.Throws<ArgumentNullException>(() => ByteDiff.Compare(null!, new byte[] { 1 }));
        Assert.Throws<ArgumentNullException>(() => ByteDiff.Compare(new byte[] { 1 }, null!));
    }
}