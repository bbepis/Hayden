CREATE TABLE IF NOT EXISTS `{0}` (
  `doc_id` int unsigned NOT NULL auto_increment,
  `media_id` int unsigned NOT NULL DEFAULT '0',
  `poster_ip` decimal(39,0) unsigned NOT NULL DEFAULT '0',
  `num` int unsigned NOT NULL,
  `subnum` int unsigned NOT NULL,
  `thread_num` int unsigned NOT NULL DEFAULT '0',
  `op` bool NOT NULL DEFAULT '0',
  `timestamp` int unsigned NOT NULL,
  `timestamp_expired` int unsigned NOT NULL,
  `preview_orig` varchar(20),
  `preview_w` smallint unsigned NOT NULL DEFAULT '0',
  `preview_h` smallint unsigned NOT NULL DEFAULT '0',
  `media_filename` text,
  `media_w` smallint unsigned NOT NULL DEFAULT '0',
  `media_h` smallint unsigned NOT NULL DEFAULT '0',
  `media_size` int unsigned NOT NULL DEFAULT '0',
  `media_hash` varchar(25),
  `media_orig` varchar(20),
  `spoiler` bool NOT NULL DEFAULT '0',
  `deleted` bool NOT NULL DEFAULT '0',
  `capcode` varchar(1) NOT NULL DEFAULT 'N',
  `email` varchar(100),
  `name` varchar(100),
  `trip` varchar(25),
  `title` varchar(100),
  `comment` text,
  `delpass` tinytext,
  `sticky` bool NOT NULL DEFAULT '0',
  `locked` bool NOT NULL DEFAULT '0',
  `poster_hash` varchar(8),
  `poster_country` varchar(2),
  `exif` text,

  PRIMARY KEY (`doc_id`),
  UNIQUE num_subnum_index (`num`, `subnum`),
  INDEX thread_num_subnum_index (`thread_num`, `num`, `subnum`),
  INDEX subnum_index (`subnum`),
  INDEX op_index (`op`),
  INDEX media_id_index (`media_id`),
  INDEX media_hash_index (`media_hash`),
  INDEX media_orig_index (`media_orig`),
  INDEX name_trip_index (`name`, `trip`),
  INDEX trip_index (`trip`),
  INDEX email_index (`email`),
  INDEX poster_ip_index (`poster_ip`),
  INDEX timestamp_index (`timestamp`)
) engine=InnoDB CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;


CREATE TABLE IF NOT EXISTS `{0}_deleted` LIKE `{0}`;


CREATE TABLE IF NOT EXISTS `{0}_threads` (
  `thread_num` int unsigned NOT NULL,
  `time_op` int unsigned NOT NULL,
  `time_last` int unsigned NOT NULL,
  `time_bump` int unsigned NOT NULL,
  `time_ghost` int unsigned DEFAULT NULL,
  `time_ghost_bump` int unsigned DEFAULT NULL,
  `time_last_modified` int unsigned NOT NULL,
  `nreplies` int unsigned NOT NULL DEFAULT '0',
  `nimages` int unsigned NOT NULL DEFAULT '0',
  `sticky` bool NOT NULL DEFAULT '0',
  `locked` bool NOT NULL DEFAULT '0',

  PRIMARY KEY (`thread_num`),
  INDEX time_op_index (`time_op`),
  INDEX time_bump_index (`time_bump`),
  INDEX time_ghost_bump_index (`time_ghost_bump`),
  INDEX time_last_modified_index (`time_last_modified`),
  INDEX sticky_index (`sticky`),
  INDEX locked_index (`locked`)
) ENGINE=InnoDB CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;


CREATE TABLE IF NOT EXISTS `{0}_users` (
  `user_id` int unsigned NOT NULL auto_increment,
  `name` varchar(100) NOT NULL DEFAULT '',
  `trip` varchar(25) NOT NULL DEFAULT '',
  `firstseen` int(11) NOT NULL,
  `postcount` int(11) NOT NULL,

  PRIMARY KEY (`user_id`),
  UNIQUE name_trip_index (`name`, `trip`),
  INDEX firstseen_index (`firstseen`),
  INDEX postcount_index (`postcount`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;


CREATE TABLE IF NOT EXISTS `{0}_images` (
  `media_id` int unsigned NOT NULL auto_increment,
  `media_hash` varchar(25) NOT NULL,
  `media` varchar(20),
  `preview_op` varchar(20),
  `preview_reply` varchar(20),
  `total` int(10) unsigned NOT NULL DEFAULT '0',
  `banned` smallint unsigned NOT NULL DEFAULT '0',

  PRIMARY KEY (`media_id`),
  UNIQUE media_hash_index (`media_hash`),
  INDEX total_index (`total`),
  INDEX banned_index (`banned`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;


CREATE TABLE IF NOT EXISTS `{0}_daily` (
  `day` int(10) unsigned NOT NULL,
  `posts` int(10) unsigned NOT NULL,
  `images` int(10) unsigned NOT NULL,
  `sage` int(10) unsigned NOT NULL,
  `anons` int(10) unsigned NOT NULL,
  `trips` int(10) unsigned NOT NULL,
  `names` int(10) unsigned NOT NULL,

  PRIMARY KEY (`day`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
$

DROP PROCEDURE IF EXISTS `update_thread_{0}`;

$
CREATE PROCEDURE `update_thread_{0}` (tnum INT)
BEGIN
  UPDATE
    `{0}_threads` op
  SET
    op.time_last = (
      COALESCE(GREATEST(
        op.time_op,
        (SELECT MAX(timestamp) FROM `{0}` re FORCE INDEX(thread_num_subnum_index) WHERE
          re.thread_num = tnum AND re.subnum = 0)
      ), op.time_op)
    ),
    op.time_bump = (
      COALESCE(GREATEST(
        op.time_op,
        (SELECT MAX(timestamp) FROM `{0}` re FORCE INDEX(thread_num_subnum_index) WHERE
          re.thread_num = tnum AND (re.email <> 'sage' OR re.email IS NULL)
          AND re.subnum = 0)
      ), op.time_op)
    ),
    op.time_ghost = (
      SELECT MAX(timestamp) FROM `{0}` re FORCE INDEX(thread_num_subnum_index) WHERE
        re.thread_num = tnum AND re.subnum <> 0
    ),
    op.time_ghost_bump = (
      SELECT MAX(timestamp) FROM `{0}` re FORCE INDEX(thread_num_subnum_index) WHERE
        re.thread_num = tnum AND re.subnum <> 0 AND (re.email <> 'sage' OR
          re.email IS NULL)
    ),
    op.time_last_modified = (
      COALESCE(GREATEST(
        op.time_op,
        (SELECT GREATEST(MAX(timestamp), MAX(timestamp_expired)) FROM `{0}` re FORCE INDEX(thread_num_subnum_index) WHERE
          re.thread_num = tnum)
      ), op.time_op)
    ),
    op.nreplies = (
      SELECT COUNT(*) FROM `{0}` re FORCE INDEX(thread_num_subnum_index) WHERE
        re.thread_num = tnum
    ),
    op.nimages = (
      SELECT COUNT(media_hash) FROM `{0}` re FORCE INDEX(thread_num_subnum_index) WHERE
        re.thread_num = tnum
    )
    WHERE op.thread_num = tnum;
END $


DROP PROCEDURE IF EXISTS `create_thread_{0}`; $


CREATE PROCEDURE `create_thread_{0}` (num INT, timestamp INT)
BEGIN
  INSERT IGNORE INTO `{0}_threads` VALUES (num, timestamp, timestamp,
    timestamp, NULL, NULL, timestamp, 0, 0, 0, 0);
END; $


DROP PROCEDURE IF EXISTS `delete_thread_{0}`; $


CREATE PROCEDURE `delete_thread_{0}` (tnum INT)
BEGIN
  DELETE FROM `{0}_threads` WHERE thread_num = tnum;
END; $


DROP PROCEDURE IF EXISTS `insert_image_{0}`; $


CREATE PROCEDURE `insert_image_{0}` (n_media_hash VARCHAR(25),
 n_media VARCHAR(20), n_preview VARCHAR(20), n_op INT)
BEGIN
  IF n_op = 1 THEN
    INSERT INTO `{0}_images` (media_hash, media, preview_op, total)
    VALUES (n_media_hash, n_media, n_preview, 1)
    ON DUPLICATE KEY UPDATE
      media_id = LAST_INSERT_ID(media_id),
      total = (total + 1),
      preview_op = COALESCE(preview_op, VALUES(preview_op)),
      media = COALESCE(media, VALUES(media));
  ELSE
    INSERT INTO `{0}_images` (media_hash, media, preview_reply, total)
    VALUES (n_media_hash, n_media, n_preview, 1)
    ON DUPLICATE KEY UPDATE
      media_id = LAST_INSERT_ID(media_id),
      total = (total + 1),
      preview_reply = COALESCE(preview_reply, VALUES(preview_reply)),
      media = COALESCE(media, VALUES(media));
  END IF;
END; $


DROP PROCEDURE IF EXISTS `delete_image_{0}`; $


CREATE PROCEDURE `delete_image_{0}` (n_media_id INT)
BEGIN
  UPDATE `{0}_images` SET total = (total - 1) WHERE media_id = n_media_id;
END; $


DROP PROCEDURE IF EXISTS `insert_post_{0}`; $


CREATE PROCEDURE `insert_post_{0}` (p_timestamp INT, p_media_hash VARCHAR(25),
  p_email VARCHAR(100), p_name VARCHAR(100), p_trip VARCHAR(25))
BEGIN
  DECLARE d_day INT;
  DECLARE d_image INT;
  DECLARE d_sage INT;
  DECLARE d_anon INT;
  DECLARE d_trip INT;
  DECLARE d_name INT;

  SET d_day = FLOOR(p_timestamp/86400)*86400;
  SET d_image = p_media_hash IS NOT NULL;
  SET d_sage = COALESCE(p_email = 'sage', 0);
  SET d_anon = COALESCE(p_name = 'Anonymous' AND p_trip IS NULL, 0);
  SET d_trip = p_trip IS NOT NULL;
  SET d_name = COALESCE(p_name <> 'Anonymous' AND p_trip IS NULL, 1);

  INSERT INTO `{0}_daily` VALUES(d_day, 1, d_image, d_sage, d_anon, d_trip,
    d_name)
    ON DUPLICATE KEY UPDATE posts=posts+1, images=images+d_image,
    sage=sage+d_sage, anons=anons+d_anon, trips=trips+d_trip,
    names=names+d_name;

  IF (SELECT trip FROM `{0}_users` WHERE trip = p_trip) IS NOT NULL THEN
    UPDATE `{0}_users` SET postcount=postcount+1,
        firstseen = LEAST(p_timestamp, firstseen),
        name = COALESCE(p_name, '')
      WHERE trip = p_trip;
  ELSE
    INSERT INTO `{0}_users` VALUES(
    NULL, COALESCE(p_name,''), COALESCE(p_trip,''), p_timestamp, 1)
    ON DUPLICATE KEY UPDATE postcount=postcount+1,
      firstseen = LEAST(VALUES(firstseen), firstseen),
      name = COALESCE(p_name, '');
  END IF;
END; $


DROP PROCEDURE IF EXISTS `delete_post_{0}`; $


CREATE PROCEDURE `delete_post_{0}` (p_timestamp INT, p_media_hash VARCHAR(25), p_email VARCHAR(100), p_name VARCHAR(100), p_trip VARCHAR(25))
BEGIN
  DECLARE d_day INT;
  DECLARE d_image INT;
  DECLARE d_sage INT;
  DECLARE d_anon INT;
  DECLARE d_trip INT;
  DECLARE d_name INT;

  SET d_day = FLOOR(p_timestamp/86400)*86400;
  SET d_image = p_media_hash IS NOT NULL;
  SET d_sage = COALESCE(p_email = 'sage', 0);
  SET d_anon = COALESCE(p_name = 'Anonymous' AND p_trip IS NULL, 0);
  SET d_trip = p_trip IS NOT NULL;
  SET d_name = COALESCE(p_name <> 'Anonymous' AND p_trip IS NULL, 1);

  UPDATE `{0}_daily` SET posts=posts-1, images=images-d_image,
    sage=sage-d_sage, anons=anons-d_anon, trips=trips-d_trip,
    names=names-d_name WHERE day = d_day;

  IF (SELECT trip FROM `{0}_users` WHERE trip = p_trip) IS NOT NULL THEN
    UPDATE `{0}_users` SET postcount = postcount-1 WHERE trip = p_trip;
  ELSE
    UPDATE `{0}_users` SET postcount = postcount-1 WHERE
      name = COALESCE(p_name, '') AND trip = COALESCE(p_trip, '');
  END IF;
END; $


DROP TRIGGER IF EXISTS `before_ins_{0}`; $


CREATE TRIGGER `before_ins_{0}` BEFORE INSERT ON `{0}`
FOR EACH ROW
BEGIN
  IF NEW.media_hash IS NOT NULL THEN
    CALL insert_image_{0}(NEW.media_hash, NEW.media_orig, NEW.preview_orig, NEW.op);
    SET NEW.media_id = LAST_INSERT_ID();
  END IF;
END; $


DROP TRIGGER IF EXISTS `after_ins_{0}`; $


CREATE TRIGGER `after_ins_{0}` AFTER INSERT ON `{0}`
FOR EACH ROW
BEGIN
  IF NEW.op = 1 THEN
    CALL create_thread_{0}(NEW.num, NEW.timestamp);
  END IF;
  CALL update_thread_{0}(NEW.thread_num);
  CALL insert_post_{0}(NEW.timestamp, NEW.media_hash, NEW.email, NEW.name, NEW.trip);
END; $


DROP TRIGGER IF EXISTS `after_del_{0}`; $


CREATE TRIGGER `after_del_{0}` AFTER DELETE ON `{0}`
FOR EACH ROW
BEGIN
  CALL update_thread_{0}(OLD.thread_num);
  IF OLD.op = 1 THEN
    CALL delete_thread_{0}(OLD.num);
  END IF;
  CALL delete_post_{0}(OLD.timestamp, OLD.media_hash, OLD.email, OLD.name, OLD.trip);
  IF OLD.media_hash IS NOT NULL THEN
    CALL delete_image_{0}(OLD.media_id);
  END IF;
END