// @ts-check
// A phonemizer that sings each lyric as one phoneme. Edit and save: with Packages > Live Reload Packages
// on in the developer window, OpenUtau reloads it. console.log prints to the developer window.
// The API is in openutau.d.ts.

/** @type {import("openutau").Info} */
export const info = {
    name: "{{id}}",
    tag: "{{tag}}",
    language: "",
};

/** @type {import("openutau").Singer | null} */
let singer = null;

/** @param {import("openutau").Singer} s */
export function setSinger(s) {
    singer = s;
}

/**
 * Called for each group of notes: a note and the "+" notes extending it.
 * @param {import("openutau").Note[]} notes
 * @param {import("openutau").Note | null} prev
 * @param {import("openutau").Note | null} next
 * @param {import("openutau").Note | null} prevNeighbour The previous note, if it ends where this one starts.
 * @param {import("openutau").Note | null} nextNeighbour The next note, if it starts where this one ends.
 * @param {import("openutau").Note[]} prevNeighbours
 * @returns {import("openutau").Result}
 */
export function process(notes, prev, next, prevNeighbour, nextNeighbour, prevNeighbours) {
    const note = notes[0];
    const lyric = note.phoneticHint ?? note.lyric;
    const oto = singer?.tryGetMappedOto(lyric, note.tone);
    return { phonemes: [{ phoneme: oto ? oto.alias : lyric }] };
}
