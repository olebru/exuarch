try { document.documentElement.dataset.theme = localStorage.getItem('exuarch.theme') === 'light' ? 'light' : 'dark'; }
catch (e) { document.documentElement.dataset.theme = 'dark'; }

document.addEventListener('DOMContentLoaded', function () {
    const url = 'https://www.exuarch.com';
    const send = document.getElementById('send');
    const done = document.getElementById('done');
    function copied() { done.hidden = false; }
    send.addEventListener('click', function () {
        if (navigator.share) {
            navigator.share({ title: 'ExµArch', text: 'ExµArch, a computer architecture playground: open it on a computer.', url: url })
                .catch(function ignoreShareSheetClosedWithoutSharing() { });
        } else if (navigator.clipboard) {
            navigator.clipboard.writeText(url).then(copied, function ignoreClipboardRejectedAddressStaysOnPage() { });
        }
    });
});
