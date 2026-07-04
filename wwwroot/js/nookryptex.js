// Rotate the selected value of each wheel into the center window (progressive
// enhancement; the page works without it).
window.nkxScrollCenter = function () {
  document.querySelectorAll('.nkx .nkx-item.sel').forEach(function (el) {
    el.scrollIntoView({ block: 'center', behavior: 'smooth' });
  });
};
