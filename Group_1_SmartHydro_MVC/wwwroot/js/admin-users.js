(function () {
    'use strict';

    /* Builds the extra line in the pop-up that warns how many sites the account owns. */
    function describeSites(count) {
        if (count === 1) {
            return 'The 1 site it owns, and its structures, will be moved to deleted items.';
        }

        if (count > 1) {
            return 'The ' + count + ' sites it owns, and their structures, will be moved to deleted items.';
        }

        return '';
    }

    /* Fills the delete pop-up with the chosen account and points its form at that account before showing it. */
    function openDeleteModal(button, parts) {
        var id = button.getAttribute('data-delete-user-id');
        var email = button.getAttribute('data-delete-user-email') || '';
        var sites = parseInt(button.getAttribute('data-delete-user-sites') || '0', 10);

        parts.form.action = parts.form.getAttribute('data-action-base') + '/' + encodeURIComponent(id);
        parts.email.value = email;
        parts.name.textContent = email;
        parts.sites.textContent = describeSites(sites);
        parts.modal.show();
    }

    var statusLabels = {
        Approved: 'Approved',
        PendingApproval: 'Pending',
        Denied: 'Denied',
        Disabled: 'Disabled'
    };

    var actionLabels = {
        approve: 'Approve (creates their starting site and emails them)',
        deny: 'Deny (emails them)',
        deactivate: 'Disable (signs them out and blocks sign-in)',
        reactivate: 'Re-enable (lets them sign in again)',
        reopen: 'Move back to pending'
    };

    /* Turns a stored status into the wording shown to people. */
    function statusLabel(status) {
        return statusLabels[status] || status;
    }

    /* Fills the status list with "keep" plus the moves the account's current status allows. */
    function fillStatusOptions(select, status, options) {
        select.innerHTML = '';

        var keep = document.createElement('option');
        keep.value = '';
        keep.textContent = 'Keep as ' + statusLabel(status);
        select.appendChild(keep);

        options.forEach(function (action) {
            if (!actionLabels[action]) {
                return;
            }

            var option = document.createElement('option');
            option.value = action;
            option.textContent = actionLabels[action];
            select.appendChild(option);
        });

        return select.options.length > 1;
    }

    function lockFields(parts, isProtected, isSelf, role) {
        var roleLocked = isProtected;

        parts.roleField.hidden = roleLocked;
        parts.role.disabled = roleLocked;

        parts.roleLocked.value = role;
        if (roleLocked) {
            parts.roleLocked.setAttribute('name', 'role');
        } else {
            parts.roleLocked.removeAttribute('name');
        }

        parts.note.hidden = !isProtected;
        if (isSelf) {
            parts.note.textContent = 'This is your account. You can change your name, but not your own role or status.';
        } else {
            parts.note.textContent = 'This is a built-in administrator, so only the name can be changed here.';
        }
    }

    /* Only approved accounts can be made admins, so the Admin choice is greyed out for anyone else. */
    function updateAdminChoice(parts, role, status) {
        var allowed = role === 'Admin' || status === 'Approved';

        parts.adminOption.disabled = !allowed;
        if (allowed) {
            parts.adminOption.textContent = 'Admin';
        } else {
            parts.adminOption.textContent = 'Admin (approve the account first)';
        }
    }

    /* Explains what making someone an admin means, but only when the role is actually being changed to Admin. */
    function updateRoleHint(parts) {
        parts.roleHint.hidden = parts.role.disabled || parts.role.value !== 'Admin' || parts.currentRole === 'Admin';
    }

    /* Shows the status list only when there is a change to pick, and hides the role and status row when both are hidden. */
    function showStatusField(parts, hasChoices) {
        parts.statusField.hidden = !hasChoices;
        parts.status.disabled = !hasChoices;
        parts.row.hidden = parts.roleField.hidden && parts.statusField.hidden;
    }

    /* Shows the starting site name only for operators still waiting on a decision, and requires it then, because approval creates that site. */
    function updateSiteField(parts) {
        var awaitingReview = parts.currentStatus === 'PendingApproval' || parts.currentStatus === 'Denied';
        var isOperator = parts.role.value === 'Operator';
        var needed = awaitingReview && isOperator && parts.status.value !== 'deny';

        parts.farmField.hidden = !needed;
        parts.farm.required = needed;
    }

    /* Fills the edit pop-up with the chosen account and points its form at that account before showing it. */
    function openEditModal(button, parts) {
        var id = button.getAttribute('data-edit-user-id');
        var role = button.getAttribute('data-edit-user-role') || 'Operator';
        var status = button.getAttribute('data-edit-user-status') || '';
        var isProtected = button.getAttribute('data-edit-user-protected') === 'true';
        var isSelf = button.getAttribute('data-edit-user-self') === 'true';
        var options = (button.getAttribute('data-edit-user-options') || '').split(',').filter(function (value) {
            return value.length > 0;
        });

        parts.form.action = parts.form.getAttribute('data-action-base') + '/' + encodeURIComponent(id);
        parts.name.value = button.getAttribute('data-edit-user-name') || '';
        parts.email.value = button.getAttribute('data-edit-user-email') || '';
        parts.farm.value = button.getAttribute('data-edit-user-farm') || '';
        parts.role.value = role;
        parts.text.textContent = parts.email.value;

        lockFields(parts, isProtected, isSelf, role);
        updateAdminChoice(parts, role, status);
        showStatusField(parts, fillStatusOptions(parts.status, status, options));
        parts.currentStatus = status;
        parts.currentRole = role;
        updateSiteField(parts);
        updateRoleHint(parts);
        parts.modal.show();
    }

    /* Wires every Edit button on the page to the shared edit pop-up. */
    function setUpEditModal() {
        var modalElement = document.getElementById('sh-edit-user-modal');
        if (!modalElement || typeof bootstrap === 'undefined') {
            return;
        }

        var role = document.getElementById('sh-edit-role');
        var parts = {
            modal: bootstrap.Modal.getOrCreateInstance(modalElement),
            form: document.getElementById('sh-edit-user-form'),
            text: document.getElementById('sh-edit-user-text'),
            name: document.getElementById('sh-edit-name'),
            email: document.getElementById('sh-edit-email'),
            farm: document.getElementById('sh-edit-farm'),
            farmField: document.getElementById('sh-edit-farm-field'),
            currentStatus: '',
            currentRole: '',
            role: role,
            adminOption: document.getElementById('sh-edit-role-admin'),
            roleHint: document.getElementById('sh-edit-role-hint'),
            roleField: document.getElementById('sh-edit-role-field'),
            statusField: document.getElementById('sh-edit-status-field'),
            row: document.getElementById('sh-edit-row'),
            roleLocked: document.getElementById('sh-edit-role-locked'),
            status: document.getElementById('sh-edit-status'),
            note: document.getElementById('sh-edit-note')
        };

        parts.role.addEventListener('change', function () {
            updateSiteField(parts);
            updateRoleHint(parts);
        });
        parts.status.addEventListener('change', function () {
            updateSiteField(parts);
        });

        document.querySelectorAll('[data-edit-user-id]').forEach(function (button) {
            button.addEventListener('click', function () {
                openEditModal(button, parts);
            });
        });

        parts.form.addEventListener('submit', function () {
            parts.form.querySelectorAll('button[type="submit"]').forEach(function (button) {
                button.disabled = true;
            });
        });
    }

    /* Checks one table row against the chosen role, status and search words. */
    function rowMatches(row, role, status, words) {
        if (role !== 'all' && row.getAttribute('data-role') !== role) {
            return false;
        }

        if (status !== 'all' && row.getAttribute('data-status') !== status) {
            return false;
        }

        var text = row.getAttribute('data-search') || '';
        return words.every(function (word) {
            return text.indexOf(word) !== -1;
        });
    }

    /* Keeps the address bar and every form's return address in step with the filters, so a refresh or an action comes back to the same view. */
    function rememberFilters(form, role, status, query) {
        var params = new URLSearchParams();
        if (role !== 'all') {
            params.set('role', role);
        }
        if (status !== 'all') {
            params.set('status', status);
        }
        if (query.length > 0) {
            params.set('q', query);
        }

        var search = params.toString();
        var url = window.location.pathname;
        if (search.length > 0) {
            url = url + '?' + search;
        }

        if (window.history.replaceState) {
            window.history.replaceState(null, '', url);
        }

        document.querySelectorAll('input[name="returnUrl"]').forEach(function (input) {
            input.value = url;
        });
    }

    /* Filters the user table as the admin types or changes a dropdown, without reloading the page. */
    function setUpLiveFilter() {
        var form = document.getElementById('sh-adm-filter');
        var table = document.getElementById('sh-adm-users-table');
        if (!form || !table) {
            return;
        }

        var roleSelect = document.getElementById('sh-adm-role');
        var statusSelect = document.getElementById('sh-adm-status');
        var searchInput = document.getElementById('sh-adm-q');
        var showing = document.getElementById('sh-adm-showing');
        var noMatch = document.getElementById('sh-adm-no-match');
        var rows = Array.prototype.slice.call(table.querySelectorAll('tbody tr'));
        var timer = null;

        function apply() {
            var query = searchInput.value.trim();
            var words = query.toLowerCase().split(/\s+/).filter(function (word) {
                return word.length > 0;
            });
            var shown = 0;

            rows.forEach(function (row) {
                var match = rowMatches(row, roleSelect.value, statusSelect.value, words);
                row.hidden = !match;
                if (match) {
                    shown = shown + 1;
                }
            });

            var word = 'users';
            if (rows.length === 1) {
                word = 'user';
            }

            showing.textContent = 'Showing ' + shown + ' of ' + rows.length + ' ' + word;
            table.hidden = shown === 0;
            noMatch.hidden = shown !== 0;
            rememberFilters(form, roleSelect.value, statusSelect.value, query);
        }

        form.addEventListener('submit', function (event) {
            event.preventDefault();
            apply();
        });
        roleSelect.addEventListener('change', apply);
        statusSelect.addEventListener('change', apply);
        searchInput.addEventListener('input', function () {
            window.clearTimeout(timer);
            timer = window.setTimeout(apply, 120);
        });

        apply();
    }

    document.addEventListener('DOMContentLoaded', setUpEditModal);
    document.addEventListener('DOMContentLoaded', setUpLiveFilter);

    document.addEventListener('DOMContentLoaded', function () {
        var modalElement = document.getElementById('sh-delete-user-modal');
        if (!modalElement || typeof bootstrap === 'undefined') {
            return;
        }

        var parts = {
            modal: bootstrap.Modal.getOrCreateInstance(modalElement),
            form: document.getElementById('sh-delete-user-form'),
            email: document.getElementById('sh-delete-user-email'),
            name: document.getElementById('sh-delete-user-name'),
            sites: document.getElementById('sh-delete-user-sites')
        };

        document.querySelectorAll('[data-delete-user-id]').forEach(function (button) {
            button.addEventListener('click', function () {
                openDeleteModal(button, parts);
            });
        });

        parts.form.addEventListener('submit', function () {
            parts.form.querySelectorAll('button').forEach(function (button) {
                button.disabled = true;
            });
        });
    });
})();