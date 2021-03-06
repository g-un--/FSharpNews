ko.bindingHandlers.scroll = {
    init: function(element, valueAccessor, allBindingsAccessor) {
        var props = allBindingsAccessor().scrollOptions;
        var offset = props.offset ? props.offset : 0;
        var loadFunc = valueAccessor();
        var off = function () { $(window).off("scroll.ko.scrollHandler"); };

        element.style.visibility = "hidden";
        $(window).on("scroll.ko.scrollHandler", function () {
            if ($(document).height() - offset <= $(window).height() + $(window).scrollTop()) {
                element.style.visibility = "visible";
                loadFunc().always(function(data) {
                    element.style.visibility = "hidden";
                    if (data.length === 0)
                        off();
                });
            }
        });

        ko.utils.domNodeDisposal.addDisposeCallback(element, off);
    }
}
