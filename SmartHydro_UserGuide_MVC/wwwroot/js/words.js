document.addEventListener('DOMContentLoaded', function () {
    var finder = document.querySelector('[data-word-finder]');

    if (!finder) {
        return;
    }

    var input = document.getElementById('sh-word-search');
    var none = document.getElementById('sh-words-none');
    var groups = document.querySelectorAll('[data-word-group]');

    // Lowercases text and drops spaces, dashes and other marks, so "wifi" also finds "Wi-Fi".
    function simplify(text) {
        return text.toLowerCase().replace(/[^a-z0-9]/g, '');
    }

    // Shows only the words whose name or meaning contains the typed text, and hides groups left empty.
    function filterWords() {
        var text = simplify(input.value);
        var shownTotal = 0;

        for (var g = 0; g < groups.length; g++) {
            var words = groups[g].querySelectorAll('[data-word]');
            var shownInGroup = 0;

            for (var w = 0; w < words.length; w++) {
                var matches = text === '' || simplify(words[w].textContent).indexOf(text) !== -1;

                words[w].hidden = !matches;

                if (matches) {
                    shownInGroup++;
                }
            }

            groups[g].hidden = shownInGroup === 0;
            shownTotal += shownInGroup;
        }

        none.hidden = shownTotal > 0;
    }

    finder.hidden = false;
    input.addEventListener('input', filterWords);
});
