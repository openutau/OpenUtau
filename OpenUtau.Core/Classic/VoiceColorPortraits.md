# Voice color portraits

A voicebank can show a different picture in the piano roll for each voice color. Banks with a single portrait can keep their existing configuration.

1. Place the PNG files in your voicebank. For this example, create a folder named `portraits` beside `character.yaml` and put the four images there.
2. In `character.yaml`, keep the default image under `portrait`. Add an optional `voice_color_portraits` section. Copy the color names exactly as they appear in the singer's **Voice Color (CLR)** expression, including spaces and capital letters. Each path is relative to the voicebank folder.

   ```yaml
   portrait: portraits/normal.png
   voice_color_portraits:
     Power: portraits/power.png
     Soft: portraits/soft.png
     Whisper: portraits/whisper.png
   ```

3. Reload the singer, open its vocal part in the piano roll, and enable **Show portrait on piano roll** in appearance preferences.
4. Assign voice colors to phonemes and play the part. For example, give the first sound in one note **Power** and the second **Soft**. The picture starts changing when playback reaches each sound. **Reduce animations** makes the change immediate once an image has loaded.

The piano roll follows the primary CLR value of each phoneme in the open part, including edits to its timing. A playing phoneme with an empty or unmapped color, or a missing or unreadable image, uses the default portrait. If the default is also unavailable, that interval shows no portrait. The singer's existing portrait height and opacity settings apply to all images.

## Appearance preferences

**Advanced Portrait Settings** is enabled by default. During a gap in the open part's phoneme timeline, the previous phoneme's picture stays visible. After **225 ms** from the actual end of that sound, the picture dims to half the opacity in `character.yaml`. A bank configured at 60% dims to 30%. The next sound restores the normal opacity. The default **150 ms** fade applies to both opacity and image changes.

- Turn off **Use multiple illustrations** to show only the singer's default image.
- Adjust **Silence threshold (ms)** to change how long a gap must last. Turn off **Dim during silence** to keep the normal opacity throughout playback.
- Adjust **Fade duration (ms)** to change the transition time. Zero makes changes immediate. **Reduce animations** also makes transitions immediate.
- Turn off **Advanced Portrait Settings** to display the singer's default portrait at its configured opacity throughout playback. Enabling advanced settings restores your previous choices.

Pausing or stopping holds the selected portrait and its playback-driven brightness. An image crossfade already in progress can finish. After moving the playhead while stopped, the portrait updates when playback resumes. A singer change selects the new default and uses the new singer's own opacity. Images are cached while the singer is displayed. Reload the singer after replacing an image on disk.
