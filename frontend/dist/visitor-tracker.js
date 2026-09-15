/* visitor-tracker.js — alexvysotsky.com */
(function () {
  function uuidv4() {
    return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function(c) {
      var r = Math.random() * 16 | 0, v = c === 'x' ? r : (r & 0x3 | 0x8);
      return v.toString(16);
    });
  }

  function getCookie(name) {
    var match = document.cookie.match(new RegExp('(^| )' + name + '=([^;]+)'));
    return match ? match[2] : null;
  }

  function setCookie(name, value, days) {
    var expires = new Date(Date.now() + days * 864e5).toUTCString();
    document.cookie = name + '=' + value + '; expires=' + expires + '; path=/; SameSite=Lax';
  }

  var visitorUuid = getCookie('_van_vid');
  if (!visitorUuid) {
    visitorUuid = uuidv4();
    setCookie('_van_vid', visitorUuid, 365);
  }

  var sessionUuid = sessionStorage.getItem('_van_sid');
  if (!sessionUuid) {
    sessionUuid = uuidv4();
    sessionStorage.setItem('_van_sid', sessionUuid);
  }

  function getSessionInfo() {
    try {
      var raw = localStorage.getItem('van_session');
      if (!raw) {
        return { auth_state: 'public', is_authed: false, user_login: null, user_id: null };
      }
      var sess = JSON.parse(raw);
      var user = (sess && sess.user) || {};
      var login = user.login || user.Login || sess.login || sess.Login || null;
      var userId = user.userId || user.user_id || user.id || sess.userId || sess.user_id || sess.id || null;
      var numUserId = (userId === null || userId === undefined || userId === '') ? null : Number(userId);
      if (numUserId !== null && !isFinite(numUserId)) numUserId = null;
      return {
        auth_state: 'authenticated',
        is_authed: true,
        user_login: login ? String(login) : null,
        user_id: numUserId
      };
    } catch (e) {
      return { auth_state: 'public', is_authed: false, user_login: null, user_id: null };
    }
  }

  function track() {
    var sess = getSessionInfo();
    var payload = {
      event_uuid: uuidv4(),
      visitor_uuid: visitorUuid,
      session_uuid: sessionUuid,
      url: window.location.href,
      path: window.location.pathname || '/',
      query_string: (window.location.search || '').replace(/^\?/, ''),
      page_title: document.title || '',
      referrer: document.referrer || '',
      is_authed: !!sess.is_authed,
      auth_state: sess.auth_state,
      user_login: sess.user_login,
      user_id: sess.user_id
    };
    fetch('/visitor-api/track', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
      keepalive: true
    }).catch(function(){});
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', track);
  } else {
    track();
  }

  var _pushState = history.pushState;
  history.pushState = function() {
    _pushState.apply(this, arguments);
    setTimeout(track, 100);
  };
  var _replaceState = history.replaceState;
  history.replaceState = function() {
    _replaceState.apply(this, arguments);
    setTimeout(track, 100);
  };
  window.addEventListener('popstate', function() { setTimeout(track, 100); });
})();
