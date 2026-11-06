(function () {
    'use strict';

    var RELOAD_DELAY_MS = 1200;

    /* Returns true when nothing on the page is mid-edit, so reloading it will not lose anything the user typed. */
    function pageIsIdle() {
        if (document.querySelector('.modal.show')) {
            return false;
        }

        var active = document.activeElement;
        if (!active) {
            return true;
        }

        var tag = active.tagName;
        return tag !== 'INPUT' && tag !== 'TEXTAREA' && tag !== 'SELECT';
    }

    /* Builds the toast once and returns its parts, so later changes reuse the same element. */
    function getToast() {
        var toast = document.getElementById('sh-live-toast');
        if (toast) {
            return toast;
        }

        toast = document.createElement('div');
        toast.id = 'sh-live-toast';
        toast.className = 'sh-live-toast';
        toast.hidden = true;

        var icon = document.createElement('span');
        icon.className = 'material-symbols-outlined';
        icon.setAttribute('aria-hidden', 'true');
        icon.textContent = 'sync';

        var text = document.createElement('span');
        text.className = 'sh-live-toast-text';

        var button = document.createElement('button');
        button.type = 'button';
        button.className = 'sh-live-toast-btn';
        button.textContent = 'Refresh';
        button.addEventListener('click', function () {
            window.location.reload();
        });

        toast.appendChild(icon);
        toast.appendChild(text);
        toast.appendChild(button);
        document.body.appendChild(toast);
        return toast;
    }

    /* Shows the change in the toast, with a Refresh button when the page is not reloading by itself. */
    function showToast(message, offerRefresh) {
        var toast = getToast();
        toast.querySelector('.sh-live-toast-text').textContent = message;
        toast.querySelector('.sh-live-toast-btn').hidden = !offerRefresh;
        toast.hidden = false;

        if (window.shAnnounce) {
            window.shAnnounce(message + (offerRefresh ? ' Use the Refresh button to update the page.' : ''));
        }
    }

    /* Decides whether a change pushed from the server matters to the page that is open. */
    function isRelevant(change, body) {
        if (body.getAttribute('data-watch-all') === 'true') {
            return true;
        }

        var siteId = (body.getAttribute('data-site-id') || '').toLowerCase();
        return siteId.length > 0 && (change.siteId || '').toLowerCase() === siteId;
    }

    /* Reacts to a change another person made: reloads idle list pages, otherwise offers a refresh; a worker taken off the site, or a site that is deleted while it is open, is moved off it at once. */
    function handleChange(change, body) {
        var me = (body.getAttribute('data-user-email') || '').toLowerCase();
        if (change && change.kind === 'WorkerRemoved' && (change.subject || '').toLowerCase() === me) {
            showToast('You were removed from this site. Loading your other sites...', false);
            window.setTimeout(function () {
                window.location.reload();
            }, RELOAD_DELAY_MS);
            return;
        }

        var openSiteId = (body.getAttribute('data-site-id') || '').toLowerCase();
        if (change && change.kind === 'SiteDeleted' && !window.shSiteDeleteInProgress && openSiteId.length > 0 && (change.siteId || '').toLowerCase() === openSiteId) {
            showToast('This site was deleted. Loading your other sites...', false);
            window.setTimeout(function () {
                window.location.reload();
            }, RELOAD_DELAY_MS);
            return;
        }

        if (!change || (change.actorEmail || '').toLowerCase() === me || !isRelevant(change, body)) {
            return;
        }

        if (body.getAttribute('data-live-refresh') === 'true' && pageIsIdle()) {
            showToast(change.message + ' Updating...', false);
            window.setTimeout(function () {
                window.location.reload();
            }, RELOAD_DELAY_MS);
            return;
        }

        showToast(change.message, true);
    }

    /* Asks the hub to send this site's changes to the page. */
    function joinSite(connection, body) {
        var siteId = body.getAttribute('data-site-id');
        if (!siteId) {
            return Promise.resolve();
        }

        return connection.invoke('JoinSite', siteId).catch(function () {
            return false;
        });
    }

    document.addEventListener('DOMContentLoaded', function () {
        var body = document.body;
        var hubUrl = body.getAttribute('data-hub-url');

        if (!hubUrl || typeof signalR === 'undefined') {
            return;
        }

        var connection = new signalR.HubConnectionBuilder()
            .withUrl(hubUrl)
            .withAutomaticReconnect()
            .configureLogging(signalR.LogLevel.Warning)
            .build();

        connection.on('siteChanged', function (change) {
            handleChange(change, body);
        });

        connection.onreconnected(function () {
            joinSite(connection, body);
        });

        connection.start()
            .then(function () {
                return joinSite(connection, body);
            })
            .catch(function () {
                return null;
            });
    });
})();