// Check for jQuery if not found load it

if (typeof jQuery === 'undefined') {
    var script = document.createElement('script');
    script.src = '/libs/jquery/1.7.1/jquery.min.js';
    script.type = 'text/javascript';
    document.getElementsByTagName('head')[0].appendChild(script);
}

setTimeout(function() {
   // Track all File Daownloads
    $(document).ready(function ($) {
        var filetypes = /\.(txt|zip|exe|pdf|doc|docx|xls|xlsx|ppt|pptx|mp3|csv|tsv|ics)$/i;
        var baseHref = '';
        if ($('base').attr('href') != undefined)
            baseHref = $('base').attr('href');
        $('a').each(function () {
            var href = $(this).attr('href');
            if (href && href.match(filetypes)) {
                $(this).click(function () {
                    var extension = (/[.]/.exec(href)) ? /[^.]+$/.exec(href) : undefined;
                    //extension = extension.toUpperCase();
                    var filePath = href;
                    _gaq.push(['_trackEvent', extension + 's', 'Downloaded', filePath]);
                    if ($(this).attr('target') != undefined && $(this).attr('target').toLowerCase() != '_blank') {
                        setTimeout(function () {
                            location.href = baseHref + href;
                        }, 200);
                        return false;
                    }
                });
            }
        });
    }); 
}, 1000);


var hostname = window.location.hostname;
var host = hostname.split(".");
if (host[0] == "reg")
{
    var _gaq = _gaq || [];
    _gaq.push(['_setAccount', 'UA-20386085-1']);
    _gaq.push(['_trackPageview']);
} else if (host[0] == "www")
{
    var _gaq = _gaq || [];
    _gaq.push(['_setAccount', 'UA-3816559-1']);
    _gaq.push(['_trackPageview']);
} else if (host[0] == "wdev")
{
    var _gaq = _gaq || [];
    _gaq.push(['_setAccount', 'UA-21709175-1']);
    _gaq.push(['_trackPageview']);
}
(function () {
    var ga = document.createElement('script');
    ga.type = 'text/javascript';
    ga.async = true;
    ga.src = ('https:' == document.location.protocol ? 'https://ssl' : 'http://www') + '.google-analytics.com/ga.js';
    var s = document.getElementsByTagName('script')[0];
    s.parentNode.insertBefore(ga, s);
})();