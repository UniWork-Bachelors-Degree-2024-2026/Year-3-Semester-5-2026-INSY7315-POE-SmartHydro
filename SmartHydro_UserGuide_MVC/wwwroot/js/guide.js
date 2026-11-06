document.addEventListener('DOMContentLoaded', function () {
    var overlay = document.getElementById('sh-walk');
    var walkSteps = [];
    var walkTitle = '';
    var walkIndex = 0;
    var lastFocus = null;

    // Reads the HTML of each step on a task card, so the walkthrough shows exactly what the card shows.
    function readSteps(card) {
        var items = card.querySelectorAll('.sh-steps .sh-step-text');
        var steps = [];

        for (var i = 0; i < items.length; i++) {
            steps.push(items[i].innerHTML);
        }

        return steps;
    }

    // Draws the current step: the counter, progress bar, step number and text, and the Back and Next buttons.
    function drawWalk() {
        var total = walkSteps.length;
        var next = document.getElementById('sh-walk-next');
        var back = document.getElementById('sh-walk-back');

        document.getElementById('sh-walk-count').textContent = 'Step ' + (walkIndex + 1) + ' of ' + total;
        document.getElementById('sh-walk-bar').style.width = ((walkIndex + 1) / total * 100) + '%';
        document.getElementById('sh-walk-num').textContent = String(walkIndex + 1);
        document.getElementById('sh-walk-title').textContent = walkTitle;
        document.getElementById('sh-walk-text').innerHTML = walkSteps[walkIndex];

        back.disabled = walkIndex === 0;

        if (walkIndex === total - 1) {
            next.innerHTML = '<span class="material-symbols-outlined" aria-hidden="true">check</span>Finished';
        } else {
            next.innerHTML = 'Next<span class="material-symbols-outlined" aria-hidden="true">arrow_forward</span>';
        }
    }

    // Reads the current walkthrough step aloud when read aloud is on. Opening it also announces the dialog and its title.
    function speakWalk(isOpening) {
        if (!window.shReaderSpeak) {
            return;
        }

        var message = '';

        if (isOpening) {
            message = walkTitle + '. ';
        }

        message += document.getElementById('sh-walk-count').textContent + '. ' + document.getElementById('sh-walk-text').textContent;
        window.shReaderSpeak(message);
    }

    // Opens the walkthrough for a task card at its first step and moves focus to the Next button.
    function openWalk(card) {
        walkSteps = readSteps(card);
        walkTitle = card.querySelector('.sh-task-title').textContent;
        walkIndex = 0;
        lastFocus = document.activeElement;

        overlay.hidden = false;
        document.body.classList.add('sh-walk-open');
        drawWalk();
        document.getElementById('sh-walk-next').focus();
        speakWalk(true);
    }

    // Closes the walkthrough and returns focus to the button that opened it.
    function closeWalk() {
        overlay.hidden = true;
        document.body.classList.remove('sh-walk-open');

        if (lastFocus) {
            lastFocus.focus();
        }
    }

    // Moves to the next step, or closes the walkthrough after the last one.
    function nextStep() {
        if (walkIndex < walkSteps.length - 1) {
            walkIndex++;
            drawWalk();
            speakWalk(false);
        } else {
            closeWalk();
        }
    }

    // Moves back one step, staying on the first step if already there.
    function previousStep() {
        if (walkIndex > 0) {
            walkIndex--;
            drawWalk();
            speakWalk(false);
        }
    }

    // Lets Escape close the walkthrough and the arrow keys move between steps while it is open.
    function handleWalkKeys(event) {
        if (overlay.hidden) {
            return;
        }

        if (event.key === 'Escape') {
            closeWalk();
        } else if (event.key === 'ArrowRight') {
            nextStep();
        } else if (event.key === 'ArrowLeft') {
            previousStep();
        }
    }

    // Closes the walkthrough when the dark area around it is clicked.
    function closeOnBackdrop(event) {
        if (event.target === overlay) {
            closeWalk();
        }
    }

    // Connects every "Show me one step at a time" button to the task card it sits in.
    function wireWalkthroughButtons() {
        var buttons = document.querySelectorAll('[data-walkthrough]');

        for (var i = 0; i < buttons.length; i++) {
            buttons[i].addEventListener('click', function (event) {
                openWalk(event.currentTarget.closest('.sh-task'));
            });
        }
    }

    // Works out the box's Wi-Fi name from the sticker code: SmartHydro-, then Setup (tower box) or Fodder, and the last four characters of the code.
    function updateHotspotName(finder) {
        var macInput = finder.querySelector('#sh-mac');
        var boxType = finder.querySelector('#sh-box-type');
        var ssid = finder.querySelector('#sh-ssid');
        var error = finder.querySelector('#sh-mac-error');

        var hex = macInput.value.replace(/[^0-9a-f]/gi, '').toUpperCase();
        var isValid = hex.length === 12;

        error.hidden = true;
        if (!isValid && macInput.value.trim() !== '') {
            error.hidden = false;
        }

        if (!isValid) {
            ssid.textContent = '...';
            return;
        }

        ssid.textContent = 'SmartHydro-' + boxType.value + '-' + hex.substring(8, 12);
    }

    // Connects the Wi-Fi name finder so its answer updates as the code or box type changes.
    function wireHotspotFinder() {
        var finder = document.querySelector('[data-hotspot-finder]');
        if (!finder) {
            return;
        }

        var update = function () {
            updateHotspotName(finder);
        };

        finder.querySelector('#sh-mac').addEventListener('input', update);
        finder.querySelector('#sh-box-type').addEventListener('change', update);
        update();
    }

    if (overlay) {
        document.getElementById('sh-walk-next').addEventListener('click', nextStep);
        document.getElementById('sh-walk-back').addEventListener('click', previousStep);
        document.getElementById('sh-walk-close').addEventListener('click', closeWalk);
        overlay.addEventListener('click', closeOnBackdrop);
        document.addEventListener('keydown', handleWalkKeys);
        wireWalkthroughButtons();
    }

    wireHotspotFinder();
});
