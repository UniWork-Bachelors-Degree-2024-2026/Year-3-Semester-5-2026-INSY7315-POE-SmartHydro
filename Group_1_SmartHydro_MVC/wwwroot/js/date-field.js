(function () {
    'use strict';

    var MONTHS = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];
    var WEEKDAYS = ['Mon', 'Tue', 'Wed', 'Thur', 'Fri', 'Sat', 'Sun'];
    var DAY_MS = 24 * 60 * 60 * 1000;

    function pad(number) {
        if (number < 10) {
            return '0' + number;
        }
        return String(number);
    }

    function parseKey(key) {
        var parts = key.split('-');
        return Date.UTC(parseInt(parts[0], 10), parseInt(parts[1], 10) - 1, parseInt(parts[2], 10));
    }

    function toKey(dayNumber) {
        var date = new Date(dayNumber);
        return date.getUTCFullYear() + '-' + pad(date.getUTCMonth() + 1) + '-' + pad(date.getUTCDate());
    }

    function displayLabel(key) {
        var date = new Date(parseKey(key));
        return pad(date.getUTCDate()) + '/' + pad(date.getUTCMonth() + 1) + '/' + date.getUTCFullYear();
    }

    function localTodayKey() {
        var now = new Date();
        return now.getFullYear() + '-' + pad(now.getMonth() + 1) + '-' + pad(now.getDate());
    }

    function create(field) {
        var hiddenInput = field.querySelector('input[type="hidden"]');
        if (!hiddenInput) {
            return null;
        }

        var placeholder = field.getAttribute('data-placeholder') || 'Select a date';
        var maxKey = field.getAttribute('data-max') || localTodayKey();
        var maxNumber = parseKey(maxKey);
        var open = false;

        var trigger = document.createElement('button');
        trigger.type = 'button';
        trigger.className = 'sh-date-trigger';
        trigger.id = hiddenInput.id + '-trigger';
        trigger.setAttribute('aria-haspopup', 'dialog');
        trigger.setAttribute('aria-expanded', 'false');

        var triggerText = document.createElement('span');
        triggerText.className = 'sh-date-trigger-text';

        var triggerIcon = document.createElement('span');
        triggerIcon.className = 'material-symbols-outlined';
        triggerIcon.setAttribute('aria-hidden', 'true');
        triggerIcon.textContent = 'calendar_month';

        trigger.appendChild(triggerText);
        trigger.appendChild(triggerIcon);

        var popover = document.createElement('div');
        popover.className = 'sh-date-popover';
        popover.setAttribute('role', 'dialog');
        popover.hidden = true;

        var nav = document.createElement('div');
        nav.className = 'sh-cal-nav';

        var prevButton = document.createElement('button');
        prevButton.type = 'button';
        prevButton.className = 'sh-cal-arrow';
        prevButton.setAttribute('aria-label', 'Previous month');
        prevButton.innerHTML = '<span class="material-symbols-outlined" aria-hidden="true">chevron_left</span>';

        var heading = document.createElement('div');
        heading.className = 'sh-cal-heading';

        var monthLabel = document.createElement('div');
        monthLabel.className = 'sh-cal-month';
        heading.appendChild(monthLabel);

        var nextButton = document.createElement('button');
        nextButton.type = 'button';
        nextButton.className = 'sh-cal-arrow';
        nextButton.setAttribute('aria-label', 'Next month');
        nextButton.innerHTML = '<span class="material-symbols-outlined" aria-hidden="true">chevron_right</span>';

        nav.appendChild(prevButton);
        nav.appendChild(heading);
        nav.appendChild(nextButton);

        var weekdaysRow = document.createElement('div');
        weekdaysRow.className = 'sh-cal-weekdays';
        weekdaysRow.setAttribute('aria-hidden', 'true');

        WEEKDAYS.forEach(function (label) {
            var span = document.createElement('span');
            span.textContent = label;
            weekdaysRow.appendChild(span);
        });

        var daysGrid = document.createElement('div');
        daysGrid.className = 'sh-cal-days';

        popover.appendChild(nav);
        popover.appendChild(weekdaysRow);
        popover.appendChild(daysGrid);

        field.appendChild(trigger);
        field.appendChild(popover);

        var view = (function () {
            var date = new Date(parseKey(localTodayKey()));
            return { year: date.getUTCFullYear(), month: date.getUTCMonth() };
        })();

        function currentKey() {
            return hiddenInput.value || '';
        }

        function updateTrigger() {
            var key = currentKey();

            if (key === '') {
                triggerText.textContent = placeholder;
                triggerText.classList.add('is-placeholder');
            } else {
                triggerText.textContent = displayLabel(key);
                triggerText.classList.remove('is-placeholder');
            }
        }

        function setViewToKey(key) {
            var date = new Date(parseKey(key));
            view.year = date.getUTCFullYear();
            view.month = date.getUTCMonth();
        }

        function renderDays() {
            var firstOfMonth = Date.UTC(view.year, view.month, 1);
            var offset = (new Date(firstOfMonth).getUTCDay() + 6) % 7;
            var daysInMonth = new Date(Date.UTC(view.year, view.month + 1, 0)).getUTCDate();
            var weeks = Math.ceil((offset + daysInMonth) / 7);
            var startNumber = Date.UTC(view.year, view.month, 1 - offset);
            var selectedKey = currentKey();
            var todayKey = localTodayKey();
            var maxDate = new Date(maxNumber);

            daysGrid.textContent = '';

            for (var i = 0; i < weeks * 7; i++) {
                var dayNumber = startNumber + i * DAY_MS;
                var date = new Date(dayNumber);
                var key = toKey(dayNumber);
                var button = document.createElement('button');

                button.type = 'button';
                button.className = 'sh-cal-day';
                button.textContent = String(date.getUTCDate());
                button.setAttribute('data-day', key);

                if (date.getUTCMonth() !== view.month) {
                    button.classList.add('is-outside');
                }

                if (key === todayKey) {
                    button.classList.add('is-today');
                }

                if (key === selectedKey) {
                    button.classList.add('is-selected');
                    button.setAttribute('aria-current', 'date');
                }

                if (dayNumber > maxNumber) {
                    button.disabled = true;
                    button.classList.add('is-disabled');
                }

                daysGrid.appendChild(button);
            }

            monthLabel.textContent = MONTHS[view.month] + ' ' + view.year;
            nextButton.disabled = view.year > maxDate.getUTCFullYear()
                || (view.year === maxDate.getUTCFullYear() && view.month >= maxDate.getUTCMonth());
        }

        function setValue(key, silent) {
            var next = key || '';

            if (next && parseKey(next) > maxNumber) {
                next = maxKey;
            }

            hiddenInput.value = next;
            updateTrigger();

            if (next) {
                setViewToKey(next);
            }

            renderDays();

            if (!silent) {
                hiddenInput.dispatchEvent(new Event('change', { bubbles: true }));
            }
        }

        function onOutsideClick(event) {
            if (!field.contains(event.target)) {
                closePopover();
            }
        }

        function onKeydown(event) {
            if (event.key === 'Escape') {
                closePopover();
                trigger.focus();
            }
        }

        function openPopover() {
            if (open) {
                return;
            }

            open = true;
            popover.hidden = false;
            field.classList.add('is-open');
            trigger.setAttribute('aria-expanded', 'true');
            setViewToKey(currentKey() || localTodayKey());
            renderDays();
            if (popover.scrollIntoView) {
                popover.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
            }
            document.addEventListener('mousedown', onOutsideClick, true);
            document.addEventListener('keydown', onKeydown, true);
        }

        function closePopover() {
            if (!open) {
                return;
            }

            open = false;
            popover.hidden = true;
            field.classList.remove('is-open');
            trigger.setAttribute('aria-expanded', 'false');
            document.removeEventListener('mousedown', onOutsideClick, true);
            document.removeEventListener('keydown', onKeydown, true);
        }

        trigger.addEventListener('click', function () {
            if (open) {
                closePopover();
            } else {
                openPopover();
            }
        });

        prevButton.addEventListener('click', function () {
            view.month -= 1;
            if (view.month < 0) {
                view.month = 11;
                view.year -= 1;
            }
            renderDays();
        });

        nextButton.addEventListener('click', function () {
            view.month += 1;
            if (view.month > 11) {
                view.month = 0;
                view.year += 1;
            }
            renderDays();
        });

        daysGrid.addEventListener('click', function (event) {
            var button = event.target.closest('.sh-cal-day');
            if (!button || button.disabled) {
                return;
            }
            setValue(button.getAttribute('data-day'));
            closePopover();
            trigger.focus();
        });

        updateTrigger();

        return {
            getValue: currentKey,
            setValue: function (key) {
                setValue(key, true);
            },
            refreshMax: function (key) {
                maxKey = key;
                maxNumber = parseKey(key);
                renderDays();
            },
            close: closePopover
        };
    }

    window.ShDateField = { create: create };
})();