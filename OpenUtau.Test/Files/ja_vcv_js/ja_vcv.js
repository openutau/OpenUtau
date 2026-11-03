// @ts-check
// Port of OpenUtau.Plugin.Builtin/JapaneseVCVPhonemizer.cs.
import { toUnicodeElements, getParentAlternate, getParentToneShift, getParentVoiceColor } from "openutau";

/** @typedef {import("openutau").Note} Note */
/** @typedef {import("openutau").Oto} Oto */
/** @typedef {import("openutau").Singer} Singer */

/** @type {import("openutau").Info} */
export const info = {
    name: "Japanese VCV Phonemizer (JavaScript)",
    tag: "JA VCV JS",
    language: "JA",
};

// The lookup table to convert a hiragana to its tail vowel.
const vowels = [
    "a=ぁ,あ,か,が,さ,ざ,た,だ,な,は,ば,ぱ,ま,ゃ,や,ら,わ,ァ,ア,カ,ガ,サ,ザ,タ,ダ,ナ,ハ,バ,パ,マ,ャ,ヤ,ラ,ワ,a",
    "e=ぇ,え,け,げ,せ,ぜ,て,で,ね,へ,べ,ぺ,め,れ,ゑ,ェ,エ,ケ,ゲ,セ,ゼ,テ,デ,ネ,ヘ,ベ,ペ,メ,レ,ヱ,e",
    "i=ぃ,い,き,ぎ,し,じ,ち,ぢ,に,ひ,び,ぴ,み,り,ゐ,ィ,イ,キ,ギ,シ,ジ,チ,ヂ,ニ,ヒ,ビ,ピ,ミ,リ,ヰ,i",
    "o=ぉ,お,こ,ご,そ,ぞ,と,ど,の,ほ,ぼ,ぽ,も,ょ,よ,ろ,を,ォ,オ,コ,ゴ,ソ,ゾ,ト,ド,ノ,ホ,ボ,ポ,モ,ョ,ヨ,ロ,ヲ,o",
    "n=ん,n",
    "u=ぅ,う,く,ぐ,す,ず,つ,づ,ぬ,ふ,ぶ,ぷ,む,ゅ,ゆ,る,ゥ,ウ,ク,グ,ス,ズ,ツ,ヅ,ヌ,フ,ブ,プ,ム,ュ,ユ,ル,ヴ,u",
    "N=ン,ng",
];

/** @type {Map<string, string>} */
const vowelLookup = new Map();
for (const line of vowels) {
    const [vowel, cvs] = line.split("=");
    for (const cv of cvs.split(",")) {
        vowelLookup.set(cv, vowel);
    }
}

/** @type {Singer} */
let singer;

/**
 * Simply stores the singer.
 * @param {Singer} s
 */
export function setSinger(s) {
    singer = s;
}

/**
 * @param {Note[]} notes
 * @param {Note | null} prev
 * @param {Note | null} next
 * @param {Note | null} prevNeighbour
 * @param {Note | null} nextNeighbour
 * @param {Note[]} prevNeighbours
 * @returns {import("openutau").Result}
 */
export function process(notes, prev, next, prevNeighbour, nextNeighbour, prevNeighbours) {
    const note = notes[0];
    const currentLyric = note.lyric.normalize(); // measures for Unicode

    if (note.phoneticHint) {
        // If a hint is present, returns the hint.
        const oto = checkOtoUntilHit([note.phoneticHint.normalize()], note);
        if (oto) {
            return { phonemes: [{ phoneme: oto.alias }] };
        }
    }

    // The alias for no previous neighbour note. For example, "- な" for "な".
    let tests = [`- ${currentLyric}`, currentLyric];
    if (prevNeighbour) {
        // If there is a previous neighbour note, first get its hint or lyric.
        let prevLyric = prevNeighbour.lyric.normalize();
        if (prevNeighbour.phoneticHint) {
            prevLyric = prevNeighbour.phoneticHint.normalize();
        }
        // Get the last unicode element of the hint or lyric. For example, "ゃ" from "きゃ" or "- きゃ".
        const unicode = toUnicodeElements(prevLyric);
        // Look up the trailing vowel. For example "a" for "ゃ".
        const vow = vowelLookup.get(unicode[unicode.length - 1] ?? "");
        if (vow !== undefined) {
            // Now replace "- な" initially set to "a な".
            tests = [`${vow} ${currentLyric}`, `* ${currentLyric}`, currentLyric, `- ${currentLyric}`];
        }
    }
    const oto = checkOtoUntilHit(tests, note);
    return { phonemes: [{ phoneme: oto ? oto.alias : currentLyric }] };
}

/**
 * @param {string[]} input
 * @param {Note} note
 * @returns {Oto | null}
 */
function checkOtoUntilHit(input, note) {
    /** @type {Partial<import("openutau").PhonemeAttributes>} */
    const attr = note.phonemeAttributes?.find(attr => attr.index === 0) ?? {};
    const color = attr.voiceColor ?? getParentVoiceColor();
    const shift = attr.toneShift ?? getParentToneShift();
    const alt = attr.alternate ?? getParentAlternate() ?? "";

    /** @type {Oto[]} */
    const otos = [];
    for (const test of input) {
        const oto = singer.tryGetMappedOto(test + alt, note.tone + shift, color)
            ?? singer.tryGetMappedOto(test, note.tone + shift, color);
        if (oto) {
            otos.push(oto);
        }
    }
    return otos.find(oto => oto.colors.includes(color)) ?? otos[0] ?? null;
}
