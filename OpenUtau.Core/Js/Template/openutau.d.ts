// Types of the OpenUtau JavaScript phonemizer API. Keep in sync with OpenUtau.Core/Js/JsPhonemizer.cs.

declare module "openutau" {
    /** What the phonemizer menu shows. Export it as `info`. */
    export interface Info {
        name: string;
        /** Short label in the track header, e.g. "JA VCV". */
        tag: string;
        author?: string;
        /** Language code, e.g. "JA". */
        language?: string;
    }

    export interface PhonemeAttributes {
        /** Index of the phoneme in the note. */
        index: number;
        consonantStretchRatio: number | null;
        /** Shifts the note tone used for oto lookup. */
        toneShift: number | null;
        /** The number suffix of duplicate aliases. */
        alternate: number | null;
        voiceColor: string | null;
    }

    export interface Note {
        /** The lyric without the part in "[]", e.g. "read" for "read[r iy d]". */
        lyric: string;
        /** The part of the lyric in "[]", e.g. "r iy d" for "read[r iy d]". */
        phoneticHint: string | null;
        /** C4 = 60. */
        tone: number;
        /** Position in the project, in ticks. */
        position: number;
        /** Duration in ticks. */
        duration: number;
        /** Per-phoneme overrides. Not guaranteed to exist or be ordered. */
        phonemeAttributes: PhonemeAttributes[] | null;
    }

    export interface Oto {
        alias: string;
        phonetic: string;
        set: string;
        prefix: string;
        suffix: string;
        /** Voice colors of the subbanks the oto belongs to. */
        colors: (string | null)[];
    }

    export interface Singer {
        id: string;
        name: string;
        found: boolean;
        /** Looks up an alias as is. */
        tryGetOto(alias: string): Oto | null;
        /** Looks up an alias after applying the prefix map for the tone and voice color. */
        tryGetMappedOto(alias: string, tone: number, color?: string | null): Oto | null;
    }

    export interface PhonemeExpression {
        abbr: string;
        value: number;
    }

    export interface Phoneme {
        /** Should match an oto alias. Tone mapping is applied afterwards. */
        phoneme: string;
        /** Position relative to the note, in ticks. Defaults to 0. */
        position?: number;
        /** Either index all phonemes of a phonemizer or none. */
        index?: number | null;
        /** Suggested expressions. The user's values override them. */
        expressions?: PhonemeExpression[] | null;
    }

    export interface Result {
        phonemes: Phoneme[];
    }

    /** Reads a text file of the package, relative to the package folder. */
    export function readText(path: string): string;
    /** Splits text into user-perceived characters. */
    export function toUnicodeElements(text: string): string[];
    export function tickToMs(tick: number): number;
    export function msToTick(ms: number): number;
    export function getParentConsonantStretchRatio(): number;
    export function getParentToneShift(): number;
    export function getParentAlternate(): number | null;
    export function getParentVoiceColor(): string;
}
