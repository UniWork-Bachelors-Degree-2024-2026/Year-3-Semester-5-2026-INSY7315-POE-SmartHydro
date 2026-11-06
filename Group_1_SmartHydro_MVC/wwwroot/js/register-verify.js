(function () {
    var resendCooldownSeconds = 60;

    // Wires the six code boxes, the verify button and the resend link on the verify email page
    function setupVerifyForm() {
        var form = document.getElementById('sh-verify-form');
        var codeInput = document.getElementById('Code');
        var boxesWrap = document.getElementById('sh-otp-boxes');
        var verifyButton = document.getElementById('sh-otp-verify');
        var resendButton = document.getElementById('sh-otp-resend');
        var resendStatus = document.getElementById('sh-otp-resend-status');

        if (!form || !codeInput || !boxesWrap || !verifyButton) {
            return;
        }

        var tokenInput = form.querySelector('input[name="__RequestVerificationToken"]');
        var boxes = Array.prototype.slice.call(boxesWrap.querySelectorAll('.sh-otp-box'));
        var resendTimer = null;
        var submitting = false;

        // Joins the digits typed into the boxes into one code
        function readCode() {
            return boxes.map(function (box) {
                return box.value;
            }).join('');
        }

        // Marks filled boxes and enables the verify button once every box has a digit
        function refreshState() {
            boxes.forEach(function (box) {
                box.classList.toggle('is-filled', box.value !== '');
            });

            verifyButton.disabled = readCode().length !== boxes.length;
        }

        // Spreads typed or pasted digits across the boxes starting at the given box
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

        // Shakes the boxes when the code is incomplete
        function shakeBoxes() {
            boxesWrap.classList.remove('is-shaking');
            void boxesWrap.offsetWidth;
            boxesWrap.classList.add('is-shaking');
        }

        // Copies the code into the hidden field and posts the form once
        function submitCode(event) {
            if (event) {
                event.preventDefault();
            }

            if (submitting) {
                return;
            }

            var code = readCode();
            if (code.length !== boxes.length) {
                shakeBoxes();
                return;
            }

            submitting = true;
            verifyButton.disabled = true;
            verifyButton.textContent = 'Verifying...';
            codeInput.value = code;
            form.submit();
        }

        // Clears every box and puts the cursor in the first one
        function clearBoxes() {
            codeInput.value = '';
            boxes.forEach(function (box) {
                box.value = '';
            });
            boxesWrap.classList.remove('has-error');
            refreshState();
            boxes[0].focus();
        }

        // Disables the resend link and counts down until another code may be requested
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

        // Shows a message under the resend link
        function showResendStatus(message) {
            if (resendStatus) {
                resendStatus.textContent = message;
            }
        }

        // Asks the server to email a new code for this sign-up
        function resendCode() {
            if (!resendButton || resendButton.disabled) {
                return;
            }

            resendButton.disabled = true;
            showResendStatus('Sending a new code...');

            var data = new FormData();
            if (tokenInput) {
                data.append('__RequestVerificationToken', tokenInput.value);
            }

            fetch(resendButton.getAttribute('data-resend-url'), {
                method: 'POST',
                body: data,
                credentials: 'same-origin'
            })
                .then(function (response) {
                    return response.json();
                })
                .then(function (result) {
                    var message = result.message;
                    if (!message) {
                        message = "A new code has been sent to your email. Check your spam folder if you don't see it.";
                    }

                    showResendStatus(message);

                    if (result.expired) {
                        resendButton.disabled = true;
                        return;
                    }

                    if (result.success === false) {
                        resendButton.disabled = false;
                        return;
                    }

                    clearBoxes();
                    startResendCooldown();
                })
                .catch(function () {
                    showResendStatus('Could not resend the code. Please try again.');
                    resendButton.disabled = false;
                });
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
                    submitCode(event);
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

        form.addEventListener('submit', submitCode);

        if (resendButton) {
            resendButton.addEventListener('click', resendCode);
        }

        if (form.querySelector('.sh-modal-error')) {
            boxesWrap.classList.add('has-error');
        } else {
            startResendCooldown();
        }

        refreshState();
        boxes[0].focus();
    }

    document.addEventListener('DOMContentLoaded', setupVerifyForm);
})();