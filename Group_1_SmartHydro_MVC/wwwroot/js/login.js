(function () {
    var focusableSelector = 'a[href], button:not([disabled]), input:not([disabled]):not([type="hidden"])';
    var lastTrigger = null;

    function openModal(modal) {
        if (!modal) {
            return;
        }

        lastTrigger = document.activeElement;
        modal.hidden = false;

        window.requestAnimationFrame(function () {
            window.requestAnimationFrame(function () {
                modal.classList.add('is-open');
            });
        });

        document.addEventListener('keydown', handleModalKeydown);
    }

    function closeModal(modal) {
        if (!modal || modal.hidden) {
            return;
        }

        modal.classList.remove('is-open');
        document.removeEventListener('keydown', handleModalKeydown);

        window.setTimeout(function () {
            modal.hidden = true;

            if (lastTrigger && typeof lastTrigger.focus === 'function') {
                lastTrigger.focus();
            }
        }, 220);
    }

    function switchModal(fromModal, toModal) {
        var originalTrigger = lastTrigger;

        fromModal.classList.remove('is-open');
        fromModal.hidden = true;
        document.removeEventListener('keydown', handleModalKeydown);

        openModal(toModal);
        lastTrigger = originalTrigger;
    }

    function getOpenModal() {
        return document.querySelector('.sh-modal.is-open');
    }

    function handleModalKeydown(event) {
        var modal = getOpenModal();
        if (!modal) {
            return;
        }

        if (event.key === 'Escape') {
            event.preventDefault();
            closeModal(modal);
            return;
        }

        if (event.key !== 'Tab') {
            return;
        }

        var focusable = Array.prototype.slice.call(modal.querySelectorAll(focusableSelector));
        if (focusable.length === 0) {
            return;
        }

        var first = focusable[0];
        var last = focusable[focusable.length - 1];

        if (event.shiftKey && document.activeElement === first) {
            event.preventDefault();
            last.focus();
        } else if (!event.shiftKey && document.activeElement === last) {
            event.preventDefault();
            first.focus();
        }
    }

    function wireCloseButtons(modal) {
        if (!modal) {
            return;
        }

        modal.querySelectorAll('[data-sh-modal-close]').forEach(function (element) {
            element.addEventListener('click', function () {
                closeModal(modal);
            });
        });
    }

    function wirePasswordToggle(toggleId, inputId) {
        var toggle = document.getElementById(toggleId);
        var input = document.getElementById(inputId);

        if (!toggle || !input) {
            return;
        }

        toggle.addEventListener('click', function () {
            var showing = input.getAttribute('type') === 'text';

            if (showing) {
                input.setAttribute('type', 'password');
                toggle.querySelector('.material-symbols-outlined').textContent = 'visibility';
                toggle.setAttribute('aria-label', 'Show password');
            } else {
                input.setAttribute('type', 'text');
                toggle.querySelector('.material-symbols-outlined').textContent = 'visibility_off';
                toggle.setAttribute('aria-label', 'Hide password');
            }
        });
    }

    function setupAuthAlert() {
        var alertBox = document.getElementById('sh-auth-alert');
        if (!alertBox) {
            return;
        }

        var closeButton = document.getElementById('sh-auth-alert-close');
        var emailInput = document.getElementById('EmailOrUsername');
        var passwordInput = document.getElementById('Password');
        var dismissed = false;

        function dismiss() {
            if (dismissed) {
                return;
            }

            dismissed = true;
            alertBox.classList.add('is-hiding');

            window.setTimeout(function () {
                if (alertBox.parentNode) {
                    alertBox.parentNode.removeChild(alertBox);
                }
            }, 300);
        }

        if (closeButton) {
            closeButton.addEventListener('click', dismiss);
        }

        if (emailInput) {
            emailInput.addEventListener('input', dismiss);
        }

        if (passwordInput) {
            passwordInput.addEventListener('input', dismiss);
        }

        window.setTimeout(dismiss, 6000);
    }

    function setupFieldErrorClearing() {
        ['EmailOrUsername', 'Password'].forEach(function (id) {
            var input = document.getElementById(id);
            if (!input) {
                return;
            }

            input.addEventListener('input', function () {
                input.classList.remove('input-validation-error');

                var message = document.querySelector('[data-valmsg-for="' + id + '"]');
                if (message) {
                    message.textContent = '';
                }
            });
        });
    }

    function setupPasswordToggle() {
        wirePasswordToggle('sh-password-toggle', 'Password');
    }

    // Waits between the automatic retries of a request that failed because the server was briefly unavailable
    var RETRY_DELAYS_MS = [2000, 4000];

    // Runs a request, and runs it again after a short pause while the server reports it is unavailable, such as when the database is waking up
    function retryWhenUnavailable(send, onRetry) {
        function attempt(retryIndex) {
            return send()
                .catch(function () {
                    return { success: false, reason: 'unavailable', message: 'Could not resend the code. Please try again.' };
                })
                .then(function (result) {
                    if (result.success || result.reason !== 'unavailable' || retryIndex >= RETRY_DELAYS_MS.length) {
                        return result;
                    }

                    if (onRetry) {
                        onRetry();
                    }

                    return new Promise(function (resolve) {
                        window.setTimeout(resolve, RETRY_DELAYS_MS[retryIndex]);
                    }).then(function () {
                        return attempt(retryIndex + 1);
                    });
                });
        }

        return attempt(0);
    }

    function setupOtpModal() {
        var modal = document.getElementById('sh-otp-modal');
        var form = document.querySelector('.sh-auth-form');
        var otpInput = document.getElementById('OtpCode');
        var boxesWrap = document.getElementById('sh-otp-boxes');
        var verifyButton = document.getElementById('sh-otp-verify');
        var reopenLink = document.getElementById('sh-otp-reopen');
        var resendButton = document.getElementById('sh-otp-resend');
        var resendStatus = document.getElementById('sh-otp-resend-status');
        var emailInput = document.getElementById('EmailOrUsername');
        var tokenInput = form.querySelector('input[name="__RequestVerificationToken"]');
        var otpStep = document.body.dataset.otpStep === 'true';

        if (!modal || !form || !otpInput || !boxesWrap || !verifyButton) {
            return;
        }

        var boxes = Array.prototype.slice.call(boxesWrap.querySelectorAll('.sh-otp-box'));
        var hasError = modal.querySelector('.sh-modal-error') !== null;

        wireCloseButtons(modal);

        function readCode() {
            return boxes.map(function (box) {
                return box.value;
            }).join('');
        }

        function refreshState() {
            boxes.forEach(function (box) {
                box.classList.toggle('is-filled', box.value !== '');
            });

            verifyButton.disabled = readCode().length !== boxes.length;
        }

        function fillFrom(startIndex, digits) {
            var index = startIndex;

            digits.split('').forEach(function (digit) {
                if (index < boxes.length) {
                    boxes[index].value = digit;
                    index++;
                }
            });

            refreshState();

            if (index >= boxes.length) {
                verifyButton.focus();
            } else {
                boxes[index].focus();
            }
        }

        function submitCode() {
            var code = readCode();
            if (code.length !== boxes.length) {
                boxesWrap.classList.remove('is-shaking');
                void boxesWrap.offsetWidth;
                boxesWrap.classList.add('is-shaking');
                return;
            }

            verifyButton.disabled = true;
            verifyButton.textContent = 'Verifying...';
            otpInput.value = code;
            form.submit();
        }

        boxes.forEach(function (box, index) {
            box.addEventListener('input', function () {
                var digits = box.value.replace(/\D/g, '');
                box.value = '';
                boxesWrap.classList.remove('has-error');

                if (digits.length === 0) {
                    refreshState();
                    return;
                }

                fillFrom(index, digits);
            });

            box.addEventListener('keydown', function (event) {
                if (event.key === 'Backspace' && box.value === '' && index > 0) {
                    event.preventDefault();
                    boxes[index - 1].value = '';
                    boxes[index - 1].focus();
                    refreshState();
                } else if (event.key === 'ArrowLeft' && index > 0) {
                    event.preventDefault();
                    boxes[index - 1].focus();
                } else if (event.key === 'ArrowRight' && index < boxes.length - 1) {
                    event.preventDefault();
                    boxes[index + 1].focus();
                } else if (event.key === 'Enter') {
                    event.preventDefault();
                    submitCode();
                }
            });

            box.addEventListener('paste', function (event) {
                var text = '';
                if (event.clipboardData) {
                    text = event.clipboardData.getData('text');
                }

                var digits = text.replace(/\D/g, '');
                if (digits.length === 0) {
                    return;
                }

                event.preventDefault();
                boxesWrap.classList.remove('has-error');
                fillFrom(index, digits);
            });

            box.addEventListener('focus', function () {
                box.select();
            });
        });

        verifyButton.addEventListener('click', submitCode);

        var resendCooldownSeconds = 60;
        var resendTimer = null;

        function startResendCooldown() {
            if (!resendButton) {
                return;
            }

            var remaining = resendCooldownSeconds;
            resendButton.disabled = true;

            window.clearInterval(resendTimer);
            resendTimer = window.setInterval(function () {
                remaining--;

                if (remaining <= 0) {
                    window.clearInterval(resendTimer);
                    resendButton.disabled = false;
                    resendButton.textContent = 'Resend code';
                } else {
                    resendButton.textContent = 'Resend code (' + remaining + 's)';
                }
            }, 1000);
        }

        function resendCode() {
            if (!resendButton || resendButton.disabled) {
                return;
            }

            var email = emailInput ? emailInput.value.trim() : '';
            if (!email) {
                return;
            }

            resendButton.disabled = true;

            if (resendStatus) {
                resendStatus.textContent = 'Sending a new code...';
            }

            var data = new FormData();
            data.append('email', email);
            if (tokenInput) {
                data.append('__RequestVerificationToken', tokenInput.value);
            }

            retryWhenUnavailable(function () {
                return fetch(modal.getAttribute('data-resend-url'), {
                    method: 'POST',
                    body: data,
                    credentials: 'same-origin'
                }).then(function (response) {
                    return response.json();
                });
            }, function () {
                if (resendStatus) {
                    resendStatus.textContent = 'Still trying...';
                }
            })
                .then(function (result) {
                    if (!result.success) {
                        if (resendStatus) {
                            resendStatus.textContent = result.message || 'Could not resend the code. Please try again.';
                        }

                        // An expired sign-in cannot be continued, so the person is taken back to the sign-in form
                        if (result.reason === 'expired') {
                            window.setTimeout(function () {
                                window.location.assign(window.location.pathname + window.location.search);
                            }, 2500);
                            return;
                        }

                        resendButton.disabled = false;
                        return;
                    }

                    if (resendStatus) {
                        resendStatus.textContent = result.message ||
                            "A new code has been sent to your email. Check your spam folder if you don't see it.";
                    }

                    otpInput.value = '';
                    boxes.forEach(function (box) {
                        box.value = '';
                    });
                    boxesWrap.classList.remove('has-error');
                    refreshState();
                    boxes[0].focus();

                    startResendCooldown();
                })
                .catch(function () {
                    if (resendStatus) {
                        resendStatus.textContent = 'Could not resend the code. Please try again.';
                    }

                    resendButton.disabled = false;
                });
        }

        if (resendButton) {
            resendButton.addEventListener('click', resendCode);
        }

        function showOtpModal() {
            otpInput.value = '';
            boxes.forEach(function (box) {
                box.value = '';
            });
            refreshState();

            if (resendStatus) {
                resendStatus.textContent = '';
            }

            openModal(modal);

            window.setTimeout(function () {
                boxes[0].focus();
            }, 60);
        }

        if (reopenLink) {
            reopenLink.addEventListener('click', function (event) {
                event.preventDefault();
                showOtpModal();
            });
        }

        if (otpStep) {
            if (hasError) {
                boxesWrap.classList.add('has-error');
            }

            window.setTimeout(showOtpModal, 80);
        }
    }

    function setupForgotPasswordModal() {
        var emailModal = document.getElementById('sh-forgot-modal');
        var codeModal = document.getElementById('sh-forgot-code-modal');
        var passwordModal = document.getElementById('sh-forgot-password-modal');
        var openLink = document.getElementById('sh-forgot-open');
        var form = document.querySelector('.sh-auth-form');

        if (!emailModal || !codeModal || !passwordModal || !openLink) {
            return;
        }

        var tokenInput = null;
        if (form) {
            tokenInput = form.querySelector('input[name="__RequestVerificationToken"]');
        }

        var emailInput = document.getElementById('sh-forgot-email');
        var emailError = document.getElementById('sh-forgot-email-error');
        var sendButton = document.getElementById('sh-forgot-send');

        var codeEmailLabel = document.getElementById('sh-forgot-code-email');
        var sentNote = document.getElementById('sh-forgot-sent-note');
        var boxesWrap = document.getElementById('sh-forgot-otp-boxes');
        var codeError = document.getElementById('sh-forgot-code-error');
        var codeStatus = document.getElementById('sh-forgot-code-status');
        var resendButton = document.getElementById('sh-forgot-resend');
        var verifyButton = document.getElementById('sh-forgot-code-verify');

        var newPasswordInput = document.getElementById('sh-forgot-new-password');
        var confirmPasswordInput = document.getElementById('sh-forgot-confirm-password');
        var resetAlertSlot = document.getElementById('sh-forgot-reset-alert-slot');
        var resetAlertTimer = null;
        var passwordStatus = document.getElementById('sh-forgot-password-status');
        var submitButton = document.getElementById('sh-forgot-reset-submit');

        if (!emailInput || !sendButton || !boxesWrap || !verifyButton ||
            !newPasswordInput || !confirmPasswordInput || !submitButton) {
            return;
        }

        var boxes = Array.prototype.slice.call(boxesWrap.querySelectorAll('.sh-otp-box'));
        var defaultSentMessage = 'If this email is linked to a SmartHydro account, a reset code is on its way. Check your spam folder if you don\'t see it.';
        var currentEmail = '';
        var resetToken = '';

        wireCloseButtons(emailModal);
        wireCloseButtons(codeModal);
        wireCloseButtons(passwordModal);
        wirePasswordToggle('sh-forgot-new-password-toggle', 'sh-forgot-new-password');
        wirePasswordToggle('sh-forgot-confirm-password-toggle', 'sh-forgot-confirm-password');

        function setText(element, text) {
            if (element) {
                element.textContent = text;
            }
        }

        function postForm(url, fields) {
            var data = new FormData();

            Object.keys(fields).forEach(function (key) {
                data.append(key, fields[key]);
            });

            if (tokenInput) {
                data.append('__RequestVerificationToken', tokenInput.value);
            }

            return fetch(url, {
                method: 'POST',
                body: data,
                credentials: 'same-origin'
            }).then(function (response) {
                return response.json();
            });
        }

        function shakeBoxes() {
            boxesWrap.classList.remove('is-shaking');
            void boxesWrap.offsetWidth;
            boxesWrap.classList.add('is-shaking');
        }

        function resetPasswordToggle(toggleId, input) {
            var toggle = document.getElementById(toggleId);
            input.setAttribute('type', 'password');

            if (!toggle) {
                return;
            }

            toggle.querySelector('.material-symbols-outlined').textContent = 'visibility';
            toggle.setAttribute('aria-label', 'Show password');
        }

        function hideResetAlert() {
            window.clearTimeout(resetAlertTimer);

            if (!resetAlertSlot) {
                return;
            }

            var alertBox = resetAlertSlot.querySelector('.sh-auth-alert');
            if (!alertBox || alertBox.classList.contains('is-hiding')) {
                return;
            }

            alertBox.classList.add('is-hiding');

            window.setTimeout(function () {
                if (alertBox.parentNode) {
                    alertBox.parentNode.removeChild(alertBox);
                }
            }, 300);
        }

        function showResetAlert(message) {
            window.clearTimeout(resetAlertTimer);

            if (!resetAlertSlot) {
                return;
            }

            resetAlertSlot.innerHTML = '';

            var alertBox = document.createElement('div');
            alertBox.className = 'sh-auth-alert';
            alertBox.setAttribute('role', 'alert');

            var icon = document.createElement('span');
            icon.className = 'material-symbols-outlined sh-auth-alert-icon';
            icon.setAttribute('aria-hidden', 'true');
            icon.textContent = 'error';

            var text = document.createElement('span');
            text.className = 'sh-auth-alert-text';
            text.textContent = message;

            var closeButton = document.createElement('button');
            closeButton.type = 'button';
            closeButton.className = 'sh-auth-alert-close';
            closeButton.setAttribute('aria-label', 'Dismiss');

            var closeIcon = document.createElement('span');
            closeIcon.className = 'material-symbols-outlined';
            closeIcon.setAttribute('aria-hidden', 'true');
            closeIcon.textContent = 'close';

            closeButton.appendChild(closeIcon);
            closeButton.addEventListener('click', hideResetAlert);

            alertBox.appendChild(icon);
            alertBox.appendChild(text);
            alertBox.appendChild(closeButton);
            resetAlertSlot.appendChild(alertBox);

            resetAlertTimer = window.setTimeout(hideResetAlert, 6000);
        }

        function readCode() {
            return boxes.map(function (box) {
                return box.value;
            }).join('');
        }

        function refreshVerifyState() {
            boxes.forEach(function (box) {
                box.classList.toggle('is-filled', box.value !== '');
            });

            verifyButton.disabled = readCode().length !== boxes.length;
        }

        function clearCodeBoxes() {
            boxes.forEach(function (box) {
                box.value = '';
            });
            boxesWrap.classList.remove('has-error');
            refreshVerifyState();
        }

        function fillFrom(startIndex, digits) {
            var index = startIndex;

            digits.split('').forEach(function (digit) {
                if (index < boxes.length) {
                    boxes[index].value = digit;
                    index++;
                }
            });

            refreshVerifyState();

            if (index >= boxes.length) {
                verifyButton.focus();
            } else {
                boxes[index].focus();
            }
        }

        function refreshSubmitState() {
            var password = newPasswordInput.value;
            var ready = password.length >= 8 && password === confirmPasswordInput.value;

            submitButton.disabled = !ready;
        }

        function resetEmailStep() {
            currentEmail = '';
            resetToken = '';
            emailInput.value = '';
            setText(emailError, '');
            sendButton.disabled = false;
            sendButton.textContent = 'Send code';
        }

        function resetCodeStep() {
            clearCodeBoxes();
            setText(codeError, '');
            setText(codeStatus, '');
            verifyButton.textContent = 'Verify';
        }

        function resetPasswordStep() {
            newPasswordInput.value = '';
            confirmPasswordInput.value = '';
            resetPasswordToggle('sh-forgot-new-password-toggle', newPasswordInput);
            resetPasswordToggle('sh-forgot-confirm-password-toggle', confirmPasswordInput);
            window.clearTimeout(resetAlertTimer);
            if (resetAlertSlot) {
                resetAlertSlot.innerHTML = '';
            }
            setText(passwordStatus, '');
            submitButton.textContent = 'Reset password';
            refreshSubmitState();
        }

        boxes.forEach(function (box, index) {
            box.addEventListener('input', function () {
                var digits = box.value.replace(/\D/g, '');
                box.value = '';
                boxesWrap.classList.remove('has-error');
                setText(codeError, '');

                if (digits.length === 0) {
                    refreshVerifyState();
                    return;
                }

                fillFrom(index, digits);
            });

            box.addEventListener('keydown', function (event) {
                if (event.key === 'Backspace' && box.value === '' && index > 0) {
                    event.preventDefault();
                    boxes[index - 1].value = '';
                    boxes[index - 1].focus();
                    refreshVerifyState();
                } else if (event.key === 'ArrowLeft' && index > 0) {
                    event.preventDefault();
                    boxes[index - 1].focus();
                } else if (event.key === 'ArrowRight' && index < boxes.length - 1) {
                    event.preventDefault();
                    boxes[index + 1].focus();
                } else if (event.key === 'Enter') {
                    event.preventDefault();
                    verifyCode();
                }
            });

            box.addEventListener('paste', function (event) {
                var text = '';
                if (event.clipboardData) {
                    text = event.clipboardData.getData('text');
                }

                var digits = text.replace(/\D/g, '');
                if (digits.length === 0) {
                    return;
                }

                event.preventDefault();
                boxesWrap.classList.remove('has-error');
                setText(codeError, '');
                fillFrom(index, digits);
            });

            box.addEventListener('focus', function () {
                box.select();
            });
        });

        var resendCooldownSeconds = 60;
        var resendTimer = null;

        function startResendCooldown() {
            if (!resendButton) {
                return;
            }

            var remaining = resendCooldownSeconds;
            resendButton.disabled = true;
            resendButton.textContent = 'Resend code (' + remaining + 's)';

            window.clearInterval(resendTimer);
            resendTimer = window.setInterval(function () {
                remaining--;

                if (remaining <= 0) {
                    window.clearInterval(resendTimer);
                    resendButton.disabled = false;
                    resendButton.textContent = 'Resend code';
                } else {
                    resendButton.textContent = 'Resend code (' + remaining + 's)';
                }
            }, 1000);
        }

        function sendCode() {
            var email = emailInput.value.trim();

            if (!email) {
                setText(emailError, 'Enter your account email.');
                return;
            }

            if (!emailInput.checkValidity()) {
                setText(emailError, 'Enter a valid email address.');
                return;
            }

            sendButton.disabled = true;
            sendButton.textContent = 'Sending...';
            setText(emailError, '');

            postForm(emailModal.getAttribute('data-request-url'), { email: email })
                .then(function (result) {
                    if (!result.success) {
                        sendButton.disabled = false;
                        sendButton.textContent = 'Send code';
                        setText(emailError, result.message || 'Could not send the code. Please try again.');
                        return;
                    }

                    currentEmail = email;
                    resetToken = '';
                    resetCodeStep();
                    setText(codeEmailLabel, email);
                    setText(sentNote, result.message || defaultSentMessage);

                    switchModal(emailModal, codeModal);
                    startResendCooldown();

                    window.setTimeout(function () {
                        boxes[0].focus();
                    }, 60);
                })
                .catch(function () {
                    sendButton.disabled = false;
                    sendButton.textContent = 'Send code';
                    setText(emailError, 'Could not send the code. Please try again.');
                });
        }

        function resendCode() {
            if (!resendButton || resendButton.disabled || !currentEmail) {
                return;
            }

            resendButton.disabled = true;
            setText(codeStatus, 'Sending a new code...');

            retryWhenUnavailable(function () {
                return postForm(emailModal.getAttribute('data-request-url'), { email: currentEmail });
            }, function () {
                setText(codeStatus, 'Still trying...');
            })
                .then(function (result) {
                    if (!result.success) {
                        resendButton.disabled = false;
                        setText(codeStatus, result.message || 'Could not resend the code. Please try again.');
                        return;
                    }

                    clearCodeBoxes();
                    setText(codeError, '');
                    setText(codeStatus, result.message || defaultSentMessage);
                    boxes[0].focus();
                    startResendCooldown();
                })
                .catch(function () {
                    resendButton.disabled = false;
                    setText(codeStatus, 'Could not resend the code. Please try again.');
                });
        }

        function verifyCode() {
            var code = readCode();

            if (code.length !== boxes.length) {
                shakeBoxes();
                return;
            }

            verifyButton.disabled = true;
            verifyButton.textContent = 'Verifying...';
            setText(codeError, '');

            postForm(codeModal.getAttribute('data-verify-url'), { email: currentEmail, code: code })
                .then(function (result) {
                    verifyButton.textContent = 'Verify';

                    if (!result.success || !result.resetToken) {
                        boxesWrap.classList.add('has-error');
                        shakeBoxes();
                        setText(codeError, result.message || 'Invalid or expired code.');
                        refreshVerifyState();
                        return;
                    }

                    resetToken = result.resetToken;
                    resetPasswordStep();
                    switchModal(codeModal, passwordModal);

                    window.setTimeout(function () {
                        newPasswordInput.focus();
                    }, 60);
                })
                .catch(function () {
                    verifyButton.textContent = 'Verify';
                    setText(codeError, 'Could not verify the code. Please try again.');
                    refreshVerifyState();
                });
        }

        function submitReset() {
            var password = newPasswordInput.value;

            if (password.length < 8) {
                showResetAlert('Password must be at least 8 characters.');
                return;
            }

            if (password !== confirmPasswordInput.value) {
                showResetAlert('Passwords do not match.');
                return;
            }

            if (!resetToken) {
                showResetAlert('Your reset session has expired. Please request a new code.');
                return;
            }

            submitButton.disabled = true;
            submitButton.textContent = 'Resetting...';
            hideResetAlert();

            postForm(passwordModal.getAttribute('data-reset-url'), {
                email: currentEmail,
                resetToken: resetToken,
                newPassword: password,
                confirmPassword: confirmPasswordInput.value
            })
                .then(function (result) {
                    if (!result.success) {
                        submitButton.disabled = false;
                        submitButton.textContent = 'Reset password';
                        showResetAlert(result.message || 'Could not reset the password. Please try again.');

                        if (result.expired) {
                            resetToken = '';
                            submitButton.disabled = true;
                        }
                        return;
                    }

                    resetToken = '';
                    setText(passwordStatus, result.message || 'Your password has been reset. You can now sign in with your new password.');

                    var loginEmail = document.getElementById('EmailOrUsername');
                    if (loginEmail && !loginEmail.value) {
                        loginEmail.value = currentEmail;
                    }

                    window.setTimeout(function () {
                        closeModal(passwordModal);
                    }, 1600);
                })
                .catch(function () {
                    submitButton.disabled = false;
                    submitButton.textContent = 'Reset password';
                    showResetAlert('Could not reset the password. Please try again.');
                });
        }

        openLink.addEventListener('click', function (event) {
            event.preventDefault();
            resetEmailStep();
            openModal(emailModal);

            window.setTimeout(function () {
                emailInput.focus();
            }, 60);
        });

        emailInput.addEventListener('input', function () {
            setText(emailError, '');
        });

        emailInput.addEventListener('keydown', function (event) {
            if (event.key === 'Enter') {
                event.preventDefault();
                sendCode();
            }
        });

        sendButton.addEventListener('click', sendCode);
        verifyButton.addEventListener('click', verifyCode);
        submitButton.addEventListener('click', submitReset);

        if (resendButton) {
            resendButton.addEventListener('click', resendCode);
        }

        [newPasswordInput, confirmPasswordInput].forEach(function (input) {
            input.addEventListener('input', function () {
                hideResetAlert();
                refreshSubmitState();
            });
        });

        [newPasswordInput, confirmPasswordInput].forEach(function (input) {
            input.addEventListener('keydown', function (event) {
                if (event.key === 'Enter' && !submitButton.disabled) {
                    event.preventDefault();
                    submitReset();
                }
            });
        });
    }

    function setupContactModal() {
        var modal = document.getElementById('sh-contact-modal');
        var link = document.getElementById('sh-contact-admin-link');
        var copyButton = document.getElementById('sh-contact-copy');
        var copiedNote = document.getElementById('sh-contact-copied');
        var emailLink = document.getElementById('sh-contact-email');

        if (!modal || !link) {
            return;
        }

        wireCloseButtons(modal);

        link.addEventListener('click', function (event) {
            event.preventDefault();
            openModal(modal);

            window.setTimeout(function () {
                if (copyButton) {
                    copyButton.focus();
                }
            }, 60);
        });

        if (!copyButton || !emailLink) {
            return;
        }

        function markCopied() {
            var icon = copyButton.querySelector('.material-symbols-outlined');
            copyButton.classList.add('is-copied');
            icon.textContent = 'check';

            if (copiedNote) {
                copiedNote.textContent = 'Email address copied to clipboard.';
            }

            window.setTimeout(function () {
                copyButton.classList.remove('is-copied');
                icon.textContent = 'content_copy';

                if (copiedNote) {
                    copiedNote.textContent = '';
                }
            }, 2000);
        }

        function fallbackCopy(text) {
            var helper = document.createElement('textarea');
            helper.value = text;
            helper.setAttribute('readonly', '');
            helper.style.position = 'fixed';
            helper.style.opacity = '0';
            document.body.appendChild(helper);
            helper.select();

            try {
                document.execCommand('copy');
                markCopied();
            } catch (error) {
                return;
            } finally {
                document.body.removeChild(helper);
            }
        }

        copyButton.addEventListener('click', function () {
            var email = emailLink.textContent.trim();

            if (navigator.clipboard && window.isSecureContext) {
                navigator.clipboard.writeText(email).then(markCopied, function () {
                    fallbackCopy(email);
                });
                return;
            }

            fallbackCopy(email);
        });
    }

    document.addEventListener('DOMContentLoaded', function () {
        setupAuthAlert();
        setupFieldErrorClearing();
        setupPasswordToggle();
        setupOtpModal();
        setupForgotPasswordModal();
        setupContactModal();
    });
})();