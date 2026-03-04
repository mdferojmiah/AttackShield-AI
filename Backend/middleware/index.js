/**
 * Middleware exports
 */

module.exports = {
  ...require('./auth'),
  ...require('./errorHandler'),
  ...require('./validation')
};
