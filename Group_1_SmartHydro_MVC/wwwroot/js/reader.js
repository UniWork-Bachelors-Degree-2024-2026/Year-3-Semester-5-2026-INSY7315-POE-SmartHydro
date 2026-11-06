/* Built-in read aloud (screen reader) for Smart Hydro.
   Uses the browser's speech synthesis, so nothing is sent anywhere and no extra software is needed.
   When it is on it: reads the page when asked, reads each control as it gets keyboard focus,
   reads dialogs as they open, and speaks the same status messages that screen readers hear. */
(function () {
    'use strict';

    var synth = window.speechSynthesis;
    var storageKey = 'sh-reader';
    var enabled = false;
    var lastSpoken = '';
    var lastSpokenAt = 0;

    var toggle = null;
    var readButton = null;

    var SKIP_TAGS = { SCRIPT: 1, STYLE: 1, NOSCRIPT: 1, TEMPLATE: 1, SVG: 1, CANVAS: 1, VIDEO: 1 };
    var CONTROL_ROLES = { button: 1, link: 1, switch: 1, checkbox: 1, radio: 1, tab: 1, menuitem: 1 };

    function readStored() {
        try {
            return localStorage.getItem(storageKey) === 'on';
        } catch (error) {
            return false;
        }
    }

    function store(isOn) {
        try {
            localStorage.setItem(storageKey, isOn ? 'on' : 'off');
        } catch (error) {
            return;
        }
    }

    /* True when the element is not shown to people, so it should not be read either. Visually hidden helper text is still read. */
    function isHidden(el) {
        if (el.hidden || el.getAttribute('aria-hidden') === 'true') {
            return true;
        }

        var style = window.getComputedStyle(el);
        return style.display === 'none' || style.visibility === 'hidden';
    }

    /* The readable text inside an element, leaving out anything hidden. */
    function textOf(node) {
        if (!node) {
            return '';
        }

        if (node.nodeType === 3) {
            return node.nodeValue;
        }

        if (node.nodeType !== 1 || SKIP_TAGS[node.tagName] || isHidden(node)) {
            return '';
        }

        if (node.tagName === 'IMG') {
            return node.getAttribute('alt') || '';
        }

        var text = '';
        node.childNodes.forEach(function (child) {
            text += ' ' + textOf(child);
        });

        return text.replace(/\s+/g, ' ').trim();
    }

    function textOfIds(ids) {
        return ids.split(/\s+/).map(function (id) {
            return textOf(document.getElementById(id));
        }).join(' ').replace(/\s+/g, ' ').trim();
    }

    // True for a link that is styled as a button, such as sh-primary-btn or sh-modal-btn-primary
    function isButtonStyled(el) {
        return Array.prototype.some.call(el.classList, function (name) {
            return /(^|-)btn(-|$)/.test(name);
        });
    }

    function roleOf(el) {
        var role = el.getAttribute('role');
        var tag = el.tagName;

        if (role) {
            return role;
        }

        if (tag === 'A') {
            if (isButtonStyled(el)) {
                return 'button';
            }

            return 'link';
        }

        if (tag === 'BUTTON') {
            return 'button';
        }

        if (tag === 'SELECT') {
            return 'drop-down list';
        }

        if (tag === 'TEXTAREA') {
            return 'text area';
        }

        if (tag === 'INPUT') {
            var type = (el.getAttribute('type') || 'text').toLowerCase();
            var types = {
                checkbox: 'checkbox', radio: 'radio button', range: 'slider', submit: 'button', button: 'button',
                reset: 'button', search: 'search field', password: 'password field', email: 'email field',
                number: 'number field', tel: 'phone number field', date: 'date field', file: 'file upload button'
            };

            return types[type] || 'text field';
        }

        return '';
    }

    /* The name a person would hear for a control: its label, its own text, then its title or placeholder. */
    function nameOf(el) {
        var labelledBy = el.getAttribute('aria-labelledby');
        if (labelledBy) {
            var fromIds = textOfIds(labelledBy);
            if (fromIds) {
                return fromIds;
            }
        }

        var ariaLabel = el.getAttribute('aria-label');
        if (ariaLabel) {
            return ariaLabel.trim();
        }

        if (el.labels && el.labels.length > 0) {
            var fromLabel = textOf(el.labels[0]);
            if (fromLabel) {
                return fromLabel;
            }
        }

        var own = '';
        if (el.tagName !== 'INPUT' && el.tagName !== 'SELECT' && el.tagName !== 'TEXTAREA') {
            own = textOf(el);
        }

        return own || el.getAttribute('title') || el.getAttribute('placeholder') || '';
    }

    /* Builds the spoken description of a control, for example "Water Pump, switch, on". */
    function describe(el) {
        var parts = [];
        var name = nameOf(el);
        var role = roleOf(el);
        var tag = el.tagName;
        var type = (el.getAttribute('type') || '').toLowerCase();

        if (name) {
            parts.push(name);
        }

        if (role) {
            parts.push(role);
        }

        if (tag === 'INPUT' && (type === 'checkbox' || type === 'radio') || role === 'switch' || role === 'checkbox') {
            if (role === 'switch') {
                parts.push(el.checked ? 'on' : 'off');
            } else {
                parts.push(el.checked ? 'checked' : 'not checked');
            }
        } else if (tag === 'SELECT' && el.selectedOptions && el.selectedOptions.length > 0) {
            parts.push(el.selectedOptions[0].text);
        } else if ((tag === 'INPUT' || tag === 'TEXTAREA') && type !== 'password' && type !== 'range' && el.value) {
            parts.push('contains ' + el.value);
        } else if (tag === 'INPUT' && type === 'range') {
            parts.push(el.value);
        }

        var expanded = el.getAttribute('aria-expanded');
        if (expanded === 'true') {
            parts.push('expanded');
        } else if (expanded === 'false') {
            parts.push('collapsed');
        }

        if (el.getAttribute('aria-pressed') === 'true') {
            parts.push('pressed');
        }

        if (el.getAttribute('aria-current')) {
            parts.push('current page');
        }

        if (el.disabled || el.getAttribute('aria-disabled') === 'true') {
            parts.push('unavailable');
        }

        if (el.required || el.getAttribute('aria-required') === 'true') {
            parts.push('required');
        }

        if (el.getAttribute('aria-invalid') === 'true') {
            parts.push('invalid entry');
        }

        var describedBy = el.getAttribute('aria-describedby');
        if (describedBy) {
            var hint = textOfIds(describedBy);
            if (hint) {
                parts.push(hint);
            }
        }

        return parts.join(', ');
    }

    function isControl(el) {
        var tag = el.tagName;

        if (tag === 'INPUT') {
            return (el.getAttribute('type') || '').toLowerCase() !== 'hidden';
        }

        return tag === 'A' || tag === 'BUTTON' || tag === 'SELECT' || tag === 'TEXTAREA' || CONTROL_ROLES[el.getAttribute('role')] === 1;
    }

    /* Splits long text into short sentences, because browsers cut off very long utterances. */
    function chunk(text) {
        var pieces = text.match(/[^.!?;:]+[.!?;:]*\s*/g) || [text];
        var chunks = [];
        var current = '';

        pieces.forEach(function (piece) {
            if (current.length + piece.length > 160 && current.length > 0) {
                chunks.push(current.trim());
                current = '';
            }

            current += piece;
        });

        if (current.trim()) {
            chunks.push(current.trim());
        }

        return chunks;
    }

    /* Speaks text. By default it stops whatever is being said so spoken feedback never lags behind the user. */
    function speak(text, queue) {
        if (!synth || !text) {
            return;
        }

        var now = Date.now();
        if (!queue && text === lastSpoken && now - lastSpokenAt < 300) {
            return;
        }

        lastSpoken = text;
        lastSpokenAt = now;

        if (!queue) {
            synth.cancel();
        }

        chunk(text).forEach(function (piece) {
            var utterance = new SpeechSynthesisUtterance(piece);
            utterance.lang = document.documentElement.lang || 'en';
            synth.speak(utterance);
        });
    }

    /* Lists what is on the page, in order, as sentences: headings, text, images and described controls. */
    function collect(root) {
        var lines = [];
        var buffer = '';

        function flush() {
            var text = buffer.replace(/\s+/g, ' ').trim();
            if (text) {
                lines.push(text);
            }
            buffer = '';
        }

        function walk(node) {
            if (node.nodeType === 3) {
                buffer += node.nodeValue;
                return;
            }

            if (node.nodeType !== 1 || SKIP_TAGS[node.tagName] || isHidden(node)) {
                return;
            }

            if (node.id === 'sh-reader-toggle' || node.id === 'sh-reader-read') {
                return;
            }

            if (node.tagName === 'IMG') {
                buffer += ' ' + (node.getAttribute('alt') || '') + ' ';
                return;
            }

            if (/^H[1-6]$/.test(node.tagName) || node.getAttribute('role') === 'heading') {
                flush();
                lines.push('Heading. ' + textOf(node));
                return;
            }

            if (isControl(node)) {
                flush();
                lines.push(describe(node));
                return;
            }

            var isBlock = window.getComputedStyle(node).display.indexOf('inline') !== 0;
            if (isBlock) {
                flush();
            }

            if (!isBlock) {
                buffer += ' ';
            }

            node.childNodes.forEach(walk);

            if (isBlock) {
                flush();
            } else {
                buffer += ' ';
            }
        }

        walk(root);
        flush();
        return lines;
    }

    function readPage(keepSpeaking) {
        var main = document.getElementById('sh-main-content') || document.querySelector('main') || document.body;
        var title = document.title ? document.title.replace(/ - Smart Hydro$/, '') : '';

        if (!keepSpeaking) {
            synth.cancel();
        }

        speak('Page: ' + title, true);

        collect(main).forEach(function (line) {
            speak(line, true);
        });
    }

    function stop() {
        if (synth) {
            synth.cancel();
        }
    }

    function updateButtons() {
        var label = enabled ? 'Turn off read aloud' : 'Turn on read aloud';

        toggle.setAttribute('aria-pressed', String(enabled));
        toggle.setAttribute('aria-label', label);
        toggle.setAttribute('title', label);
        readButton.hidden = !enabled;
    }

    function setEnabled(isOn, announce) {
        enabled = isOn;
        store(isOn);
        updateButtons();

        if (!isOn) {
            stop();
            if (announce) {
                speak('Read aloud is off.');
            }
            return;
        }

        if (announce) {
            speak('Read aloud is on. Press Alt and R to hear this page again, or Escape to stop.');
            readPage(true);
        }
    }

    // Writes a message to the hidden live region, creating it when the page has none
    function announceToRegion(message) {
        var region = document.getElementById('sh-announcer');

        if (!region) {
            region = document.createElement('div');
            region.id = 'sh-announcer';
            region.className = 'visually-hidden';
            region.setAttribute('role', 'status');
            region.setAttribute('aria-live', 'polite');
            region.setAttribute('aria-atomic', 'true');
            document.body.appendChild(region);
        }

        region.textContent = '';
        window.setTimeout(function () {
            region.textContent = message;
        }, 50);
    }

    function init() {
        toggle = document.getElementById('sh-reader-toggle');
        readButton = document.getElementById('sh-reader-read');

        if (!toggle || !readButton) {
            return;
        }

        if (!synth || typeof SpeechSynthesisUtterance === 'undefined') {
            toggle.hidden = true;
            readButton.hidden = true;
            return;
        }

        toggle.addEventListener('click', function () {
            setEnabled(!enabled, true);
        });

        readButton.addEventListener('click', function () {
            if (synth.speaking) {
                stop();
            } else {
                readPage();
            }
        });

        /* Reads each control as keyboard focus lands on it */
        document.addEventListener('focusin', function (event) {
            if (!enabled || event.target === document.body) {
                return;
            }

            var el = event.target;
            var text = '';

            if (el.id === 'sh-main-content') {
                text = 'Main content';
            } else if (isControl(el) || el.getAttribute('role')) {
                text = describe(el);
            } else {
                text = textOf(el).slice(0, 200);
            }

            speak(text);
        });

        /* Reads a dialog's title and message when it opens */
        document.addEventListener('shown.bs.modal', function (event) {
            if (!enabled) {
                return;
            }

            var modal = event.target;
            var titleId = modal.getAttribute('aria-labelledby');
            var textId = modal.getAttribute('aria-describedby');
            var message = 'Dialog.';

            if (titleId) {
                message += ' ' + textOf(document.getElementById(titleId)) + '.';
            }

            if (textId) {
                message += ' ' + textOf(document.getElementById(textId));
            }

            speak(message);
        });

        document.addEventListener('keydown', function (event) {
            if (!enabled) {
                return;
            }

            if (event.altKey && (event.key === 'r' || event.key === 'R')) {
                event.preventDefault();
                readPage();
                return;
            }

            if (event.key === 'Escape' && synth.speaking) {
                stop();
            }
        });

        /* Status messages the site already announces to screen readers are spoken too */
        var baseAnnounce = window.shAnnounce || announceToRegion;
        window.shAnnounce = function (message) {
            if (baseAnnounce) {
                baseAnnounce(message);
            }

            if (enabled && message) {
                speak(message, true);
            }
        };

        window.addEventListener('pagehide', stop);

        enabled = readStored();
        updateButtons();

        /* Browsers may refuse to speak before the person has touched the page, so this first line can be silent after a page change */
        if (enabled) {
            speak('Page: ' + document.title.replace(/ - Smart Hydro$/, '') + '. Read aloud is on.');
        }
    }

    document.addEventListener('DOMContentLoaded', init);
})();
