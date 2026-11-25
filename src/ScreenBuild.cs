namespace LiveBackground
{
    // Decides whether an extra screen needs an immediate build check. This is
    // deliberately not an explicit "rebuild from scratch": a screen can have no
    // current manifest pointer while an identical loop is still cached, and the
    // build step must be allowed to adopt that cache.
    public static class ScreenBuild
    {
        public static bool NeedsImmediateCheck(bool usesImages, bool sameAsMain,
            bool globalImagesMode, string mediaPath)
        {
            if (usesImages || sameAsMain || globalImagesMode) return false;
            return string.IsNullOrEmpty(mediaPath);
        }
    }
}
