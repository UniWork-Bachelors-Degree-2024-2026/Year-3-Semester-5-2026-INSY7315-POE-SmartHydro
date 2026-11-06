document.addEventListener('DOMContentLoaded', function () {
    var root = document.querySelector('[data-troubleshooter]');

    if (!root) {
        return;
    }

    var panels = root.querySelectorAll('.sh-ts-panel');
    var trail = document.getElementById('sh-ts-trail');
    var backButton = document.getElementById('sh-ts-back');
    var restartButton = document.getElementById('sh-ts-restart');
    var visited = [];
    var answers = [];

    // Finds the panel for a question or fix by its step id.
    function findPanel(stepId) {
        for (var i = 0; i < panels.length; i++) {
            if (panels[i].getAttribute('data-step') === stepId) {
                return panels[i];
            }
        }

        return null;
    }

    // Writes the answers chosen so far, so people can see the path they took.
    function drawTrail() {
        if (answers.length === 0) {
            trail.textContent = '';
            trail.hidden = true;
            return;
        }

        trail.textContent = 'You said: ' + answers.join(' › ');
        trail.hidden = false;
    }

    // Shows one panel, hides the rest, updates the buttons and moves focus to the new question.
    function showStep(stepId, moveFocus) {
        var target = findPanel(stepId);

        if (!target) {
            return;
        }

        for (var i = 0; i < panels.length; i++) {
            panels[i].classList.remove('is-active');
        }

        target.classList.add('is-active');

        backButton.hidden = visited.length === 0;
        restartButton.hidden = visited.length === 0;
        drawTrail();

        if (moveFocus) {
            var heading = target.querySelector('.sh-ts-ask');

            if (heading) {
                heading.focus();
            }
        }
    }

    // Returns the step currently on screen.
    function currentStep() {
        var active = root.querySelector('.sh-ts-panel.is-active');

        if (!active) {
            return 'start';
        }

        return active.getAttribute('data-step');
    }

    // Moves forward to the question or fix an answer leads to, remembering where we came from.
    function chooseAnswer(event) {
        var link = event.currentTarget;
        var next = link.getAttribute('data-next');

        if (!findPanel(next)) {
            return;
        }

        event.preventDefault();
        visited.push(currentStep());
        answers.push(link.getAttribute('data-label'));
        showStep(next, true);
    }

    // Goes back one question and forgets the last answer.
    function goBack() {
        if (visited.length === 0) {
            return;
        }

        var previous = visited.pop();
        answers.pop();
        showStep(previous, true);
    }

    // Clears every answer and returns to the first question.
    function restart() {
        visited = [];
        answers = [];
        showStep('start', true);
    }

    // Connects every answer link so it moves through the troubleshooter instead of jumping down the page.
    function wireAnswers() {
        var links = root.querySelectorAll('.sh-ts-answer');

        for (var i = 0; i < links.length; i++) {
            links[i].addEventListener('click', chooseAnswer);
        }
    }

    root.classList.add('sh-ts-ready');
    backButton.addEventListener('click', goBack);
    restartButton.addEventListener('click', restart);
    wireAnswers();
    showStep('start', false);
});
