using System.Collections.Generic;
using LiveBackground;
using Xunit;

namespace LiveBackground.Tests
{
    public class PlaylistStateTests
    {
        [Fact]
        public void IdentityOrder_is_empty_for_no_rows()
        {
            Assert.Empty(PlaylistState.IdentityOrder(0));
            Assert.Empty(PlaylistState.IdentityOrder(-3));
        }

        [Fact]
        public void IdentityOrder_counts_from_zero()
        {
            Assert.Equal(new[] { 0, 1, 2, 3 }, PlaylistState.IdentityOrder(4));
        }

        [Fact]
        public void SortedOrder_uses_the_natural_order()
        {
            // "clip 2" must come before "clip 10".
            List<string> names = new List<string> { "clip 10.mp4", "clip 2.mp4", "clip 1.mp4" };
            Assert.Equal(new[] { 2, 1, 0 }, PlaylistState.SortedOrder(names));
        }

        [Fact]
        public void SortedOrder_leaves_short_lists_alone()
        {
            Assert.Empty(PlaylistState.SortedOrder(new List<string>()));
            Assert.Equal(new[] { 0 }, PlaylistState.SortedOrder(new List<string> { "only.mp4" }));
        }

        [Fact]
        public void SwapOrder_swaps_the_two_rows()
        {
            Assert.Equal(new[] { 0, 2, 1 }, PlaylistState.SwapOrder(3, 1, 1));
            Assert.Equal(new[] { 1, 0, 2 }, PlaylistState.SwapOrder(3, 1, -1));
        }

        [Theory]
        [InlineData(3, -1, 1)]  // no row selected
        [InlineData(3, 3, -1)]  // past the end
        [InlineData(3, 0, -1)]  // would move above the first row
        [InlineData(3, 2, 1)]   // would move below the last row
        [InlineData(0, 0, 1)]   // empty list
        public void SwapOrder_refuses_a_move_off_the_list(int count, int index, int delta)
        {
            Assert.Null(PlaylistState.SwapOrder(count, index, delta));
        }

        [Fact]
        public void Capture_reads_names_and_ticks_in_order()
        {
            List<PlaylistEntry> entries = PlaylistState.Capture(
                new List<string> { "a.mp4", "b.mp4" },
                new List<bool> { false, true });
            Assert.Equal(2, entries.Count);
            Assert.Equal("a.mp4", entries[0].Name);
            Assert.False(entries[0].Enabled);
            Assert.Equal("b.mp4", entries[1].Name);
            Assert.True(entries[1].Enabled);
        }

        [Fact]
        public void Capture_treats_a_missing_tick_as_unticked()
        {
            List<PlaylistEntry> entries = PlaylistState.Capture(
                new List<string> { "a.mp4" }, new List<bool>());
            Assert.Single(entries);
            Assert.False(entries[0].Enabled);
        }

        [Fact]
        public void PlaylistTicks_keeps_a_missing_clip_unticked()
        {
            bool[] ticks = PlaylistState.PlaylistTicks(
                new List<string> { "on.mp4", "gone.mp4" },
                new List<PlaylistEntry> { new PlaylistEntry("on.mp4", true) },
                false);
            Assert.True(ticks[0]);
            Assert.False(ticks[1]);
        }

        [Fact]
        public void PlaylistTicks_keeps_a_missing_picture_ticked()
        {
            bool[] ticks = PlaylistState.PlaylistTicks(
                new List<string> { "off.jpg", "gone.jpg" },
                new List<PlaylistEntry> { new PlaylistEntry("off.jpg", false) },
                true);
            Assert.False(ticks[0]);
            Assert.True(ticks[1]);
        }

        [Fact]
        public void PlaylistTicks_matches_names_without_regard_to_case()
        {
            bool[] ticks = PlaylistState.PlaylistTicks(
                new List<string> { "Clip.MP4" },
                new List<PlaylistEntry> { new PlaylistEntry("clip.mp4", true) },
                false);
            Assert.True(ticks[0]);
        }

        [Fact]
        public void PlaylistTicks_lets_the_first_named_entry_win()
        {
            bool[] ticks = PlaylistState.PlaylistTicks(
                new List<string> { "a.mp4" },
                new List<PlaylistEntry>
                {
                    new PlaylistEntry("a.mp4", true),
                    new PlaylistEntry("a.mp4", false),
                },
                false);
            Assert.True(ticks[0]);
        }

        [Fact]
        public void PlaylistTicks_ignores_blank_entries()
        {
            bool[] ticks = PlaylistState.PlaylistTicks(
                new List<string> { "a.mp4" },
                new List<PlaylistEntry> { null, new PlaylistEntry("", true) },
                false);
            Assert.False(ticks[0]);
        }

        [Fact]
        public void CheckedIndex_finds_the_first_ticked_row()
        {
            Assert.Equal(1, PlaylistState.CheckedIndex(new List<bool> { false, true, true }));
            Assert.Equal(-1, PlaylistState.CheckedIndex(new List<bool> { false, false }));
            Assert.Equal(-1, PlaylistState.CheckedIndex(new List<bool>()));
        }

        [Fact]
        public void PreferredIndex_falls_back_to_the_first_row()
        {
            List<string> names = new List<string> { "a.mp4", "b.mp4" };
            Assert.Equal(1, PlaylistState.PreferredIndex(names, "B.MP4"));
            Assert.Equal(0, PlaylistState.PreferredIndex(names, "gone.mp4"));
            Assert.Equal(0, PlaylistState.PreferredIndex(names, ""));
            Assert.Equal(-1, PlaylistState.PreferredIndex(new List<string>(), "a.mp4"));
        }

        [Fact]
        public void SingleTicks_ticks_exactly_one_row()
        {
            Assert.Equal(new[] { false, true, false }, PlaylistState.SingleTicks(3, 1));
            Assert.Equal(new[] { false, false, false }, PlaylistState.SingleTicks(3, -1));
            Assert.Equal(new[] { false, false }, PlaylistState.SingleTicks(2, 5));
        }

        [Fact]
        public void CopyEntries_makes_an_independent_copy_and_drops_nulls()
        {
            List<PlaylistEntry> source = new List<PlaylistEntry>
            {
                new PlaylistEntry("a.mp4", true),
                null,
                new PlaylistEntry("b.mp4", false),
            };
            List<PlaylistEntry> copy = PlaylistState.CopyEntries(source);
            Assert.Equal(2, copy.Count);
            Assert.Equal("a.mp4", copy[0].Name);
            Assert.True(copy[0].Enabled);
            Assert.Equal("b.mp4", copy[1].Name);
            Assert.False(copy[1].Enabled);
            Assert.NotSame(source[0], copy[0]);
        }

        [Fact]
        public void Reorder_follows_the_order_and_uses_the_fallback()
        {
            List<string> paths = new List<string> { "p0", "p1", "p2" };
            Assert.Equal(new[] { "p2", "p0", "p1" }, PlaylistState.Reorder(paths, new[] { 2, 0, 1 }, ""));
            Assert.Equal(new[] { "p0", "" }, PlaylistState.Reorder(paths, new[] { 0, 9 }, ""));
            Assert.Empty(PlaylistState.Reorder(paths, null, ""));
        }
    }
}
