﻿class AlertService {
    static Error(message) {
        var alert = $('<div />').attr('class', 'pmd-alert error').text(message);
        $('.pmd-alert-container.center.top').append(alert);
        setTimeout(() => alert.remove(), 10000);
    }

    static Clear() {
        $('.pmd-alert-container').html('');
    }
}
