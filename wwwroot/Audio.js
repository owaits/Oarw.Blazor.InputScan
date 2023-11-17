// This is a JavaScript module that is loaded on demand. It can export any number of
// functions, and may import other JavaScript modules if required.

export function playAudio(audioId) {
    var player = document.getElementById(audioId);
    player.load();
    player.play();
}
