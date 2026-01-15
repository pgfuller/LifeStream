function start(){ menuSet(); if (window.customStart) { customStart(); }}
		
/*
 * University of Melbourne: Expanding menu and breadcrumbs script
 *
 * Copyright 2002-2004 Web Centre, University of Melbourne
 *
 * Authors: Stephen Davies <sdavies@unimelb.edu.au>
 *          Iain Pople <i.pople@unimelb.edu.au>
 *
 * University Web Centre, The University of Melbourne
 * For more information contact web-info@webcentre.unimelb.edu.au
 *
 * See the enclosed file COPYING for license information (GPL).  If you
 * did not receive this file, see http://www.fsf.org/copyleft/gpl.html.
 
  *Modification: See line 89 in breadcrumb script.
 */

// Define some global variables
menuId = 'topmenu';
imageDir = '/watl/images/symbols/';
var menuNode = [];
// This variable prevents double running IE Mac after pressing back
var menuSetState = false;

function menuSet() {
if (!menuSetState) {	
  menuSetState = true;
  // Is browser DOM compliant?
  if (document.getElementById && document.createElement) {
	// Is there a valid menu in the document?
	if (! document.getElementById(menuId)) {
		return;
	}
    var lis = document.getElementById(menuId).getElementsByTagName('li');
    var uls = document.getElementById(menuId).getElementsByTagName('ul');
    var ans = document.getElementById(menuId).getElementsByTagName('a');
    var fpath = (document.location.pathname);
	var fpathIE = document.location.protocol+'//'+document.location.hostname+document.location.pathname;
    var b, ref, cur, k, q, z, plist, control;

	if (! window.menuParent) {
		 menuParent = null;
	}
    
    // Identify the current page and highlight it appropriately.
    for (b = 0; b < ans.length; b++) {
      ref = ans[b].getAttribute('href');
      cur = ans[b];
      if ((ref == fpath ||  ref == fpathIE || ref == menuParent
		   || ref == (document.location.protocol+'//'+document.location.hostname+menuParent) )
		   && cur.className != 'duplicate') {
        cur.className = ('current');
        // Current is Level 1 menu Item
        if (cur.parentNode.parentNode.id == menuId) {
          cur.parentNode.id = "cursec";
        }
        // Current is Level 2 menu item
        else if (cur.parentNode.parentNode.parentNode.parentNode.id == menuId) {
          cur.parentNode.parentNode.parentNode.id = "cursec";
          cur.parentNode.id = "cursubsec";
        }    
        // Current is Level 3 menu item
        else {
          cur.parentNode.parentNode.parentNode.parentNode.parentNode.id = "cursec";
          cur.parentNode.parentNode.parentNode.id = "cursubsec";
        }
		break;
      }
    }
    
    // breadcrumb script
    if (document.getElementById('breadcrumbs')) {
      var bcdiv = document.getElementById('breadcrumbs')
      var delimiter = ' > ';
      var bchref, bclink, delim, crumb;

      for (var f = 0; f < lis.length; f++) {
        if (lis[f].id || lis[f].getElementsByTagName('a')[0].className == 'current') {
          delim = document.createTextNode(delimiter);
          bcdiv.appendChild(delim);
          if (lis[f].getElementsByTagName('a')[0].className == 'current') {
			  
			  //pick up link title if available otherwise use link label
			if (lis[f].getElementsByTagName('a')[0].getAttribute('title')){
			 bclink=lis[f].getElementsByTagName('a')[0].getAttribute('title');
			 } 
			else{ 
			bclink = lis[f].getElementsByTagName('a')[0].firstChild.nodeValue;
			}
            //bclink = lis[f].getElementsByTagName('a')[0].firstChild.nodeValue;
            crumb = document.createTextNode(bclink);
            bcdiv.appendChild(crumb);
          }
          else {
            a = document.createElement('a');
            bchref = lis[f].getElementsByTagName('a')[0].getAttribute('href');
            a.setAttribute('href', bchref); 
            bclink = lis[f].getElementsByTagName('a')[0].firstChild.nodeValue;
            crumb = document.createTextNode(bclink);
            a.appendChild(crumb);
            bcdiv.appendChild(a);
          }
		  
        }
		
      }
    }
    
     // Add bullet image for LIs with no children
	 /**/
    for (k = 0; k < lis.length; k++) {
      
      if (lis[k].getElementsByTagName('ul').length < 1) {
        
        q = document.createElement('img');
        q.setAttribute('src', imageDir+'dot.gif');
        q.setAttribute('style','display: inline');
        q.setAttribute('height', '13');
        q.setAttribute('width', '12');
        q.setAttribute('alt', '');
        q.setAttribute('border', '0');
        lis[k].insertBefore(q, lis[k].firstChild);
      }
    }
    
    for (z = 0; z < uls.length; z++) {
    
      uls[z].id = z;
      menuControl(z, 'x', uls[z].parentNode.firstChild.firstChild.nodeValue);
      control = menuControl(z, 'c', uls[z].parentNode.firstChild.firstChild.nodeValue); 
      plist = uls[z].parentNode;
      
      // If not current section then UL should be collapsed
      if (!plist.id) {
        uls[z].style.display = 'none';
        control = menuNode[z+'x'];
      }
      plist.insertBefore(control, plist.firstChild);
    }
  }
}
}

function menuControl(idnum, xorc, titl) {
  var i = document.createElement('img');
  var a = document.createElement('a');  
  if (xorc == 'x') {
    i.setAttribute('src', imageDir+'plus.gif');
    i.setAttribute('alt', 'expand '+titl+' menu');
    a.setAttribute('href', 'javascript:menuExpand(\''+idnum+'\');');
    a.setAttribute('title', 'expand '+titl+' menu');
  }
  else {
    i.setAttribute('src', imageDir+'minus.gif');
    i.setAttribute('alt', 'collapse '+titl+' menu');
    a.setAttribute('href', 'javascript:menuCollapse(\''+idnum+'\');');
    a.setAttribute('title', 'collapse '+titl+' menu');
  }
  i.setAttribute('height', '13');
  i.setAttribute('width', '12');
  i.setAttribute('style','display: inline');
  i.setAttribute('border', '0');
  a.appendChild(i);

  return menuNode[idnum+xorc] = a;
}

function menuCollapse(ulid) {
 var tempsubsec;
 var ul = document.getElementById(ulid);
 ul.style.display = 'none';
 ul.parentNode.replaceChild(menuNode[ulid+'x'], menuNode[ulid+'c']);
 // Remove tempsec ID now that UL has been closed
 if (ul.parentNode.id == 'tempsec' || ul.parentNode.id == 'tempsubsec') {
  // For IE Mac
  ul.parentNode.id = '';
  // Other Browsers
  ul.parentNode.removeAttribute('id');
 }
}

function menuExpand(ulid) {
 var oldtempsec, cursec, cursubsec, cursecUl, cursubsecUl;
 var ul = document.getElementById(ulid);

 // Highlight open section
 // Level 2
 if (ul.parentNode.parentNode.parentNode.parentNode.id == menuId) {
  // Close open Sub Section
  if (oldtempsubsec = document.getElementById('tempsubsec')) {
   menuCollapse(oldtempsubsec.getElementsByTagName('ul')[0].id);   
  }
  // Close current subsection if open
  if (cursubsec = document.getElementById('cursubsec')) {
   if (cursubsecUl = cursubsec.getElementsByTagName('ul')[0]) {
	   if (cursubsecUl.style.display != 'none') {
		menuCollapse(cursubsecUl.id);
		}
   	}
  }
  // Dont set id if in current section
  if (ul.parentNode.parentNode.parentNode.id != 'cursec') {
   ul.parentNode.parentNode.parentNode.id = 'tempsec';
  }
  if (ul.parentNode.id != 'cursubsec') {
   ul.parentNode.id = 'tempsubsec';
  }
 }
 // Level 1
 else if (ul.parentNode.parentNode.id == menuId) {
  // Close previous open section
  if (oldtempsec = document.getElementById('tempsec')) {
   // Is UL part of an open section?
   if (ul.parentNode.parentNode.parentNode.id != 'tempsec') {
    menuCollapse(oldtempsec.getElementsByTagName('ul')[0].id);
   }
  }
  // Close current section if open
  if (cursec = document.getElementById('cursec')) {
   if (cursecUl = cursec.getElementsByTagName('ul')[0]) {
	   if (cursecUl.style.display != 'none') {
		menuCollapse(cursecUl.id);
	   }
   }
   // Close current subsection if open
   if (cursubsec = document.getElementById('cursubsec')) {
	   if (cursubsecUl = cursubsec.getElementsByTagName('ul')[0]) {
		   if (cursubsecUl.style.display != 'none') {
			menuCollapse(cursubsecUl.id);
			}
   		}
	}
  }
  // Close sub section too
  if (oldtempsubsec = document.getElementById('tempsubsec')) {
   oldtempsubsecUl = oldtempsubsec.getElementsByTagName('ul')[0];
   if (oldtempsubsecUl.style.display != 'none') {
    menuCollapse(oldtempsubsecUl.id);
   }
  } 
  // dont set id if it is in Current Section
  if (ul.parentNode.id != 'cursec') {
   ul.parentNode.id = 'tempsec';
  } 
  // ***
  else {
   if (cursubsec = document.getElementById('cursubsec')) {
    if (cursubsecUl = cursubsec.getElementsByTagName('ul')[0]) {
		// menuExpand(cursubsecUl.id);
		cursubsecUl.style.display = 'block';
		cursubsecUl.parentNode.replaceChild(menuNode[cursubsecUl.id+'c'], menuNode[cursubsecUl.id+'x']);
	}
   }
  }
 }
 ul.style.display = 'block';
 ul.parentNode.replaceChild(menuNode[ulid+'c'], menuNode[ulid+'x']);
}

window.onLoad=addEvent(window, 'load', start);

function addEvent(obj,evt,fn){
if(obj.addEventListener)
	obj.addEventListener(evt, fn, false);
	else if(obj.attachEvent)
		obj.attachEvent('on' + evt,fn);
}
//temp for non-linked menu items
function donothing(){}